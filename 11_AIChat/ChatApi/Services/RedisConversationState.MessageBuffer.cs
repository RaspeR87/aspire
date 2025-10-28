using System.Text.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;

public partial class RedisConversationState
{
    // Aggregates message fragments into a single coalesced message.
    // It uses a timer and size threshold to trigger a flush to Redis,
    // reducing network round-trips. Once flushing is triggered (or on disposal),
    // the buffer is drained and the fragments are sent to Redis.
    private class MessageBuffer : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly ISubscriber _subscriber;
        private readonly ILogger<RedisConversationState> _logger;
        private readonly Guid _conversationId;
        private readonly ConcurrentQueue<ClientMessageFragment> _buffer = [];
        // Invariant: Once _draining is set, no more fragments can be enqueued.
        private volatile bool _draining;
        // SemaphoreSlim ensures only one FlushAsync runs at a time.
        private readonly SemaphoreSlim _flushLock = new(1, 1);
        private readonly TimeSpan _flushInterval = TimeSpan.FromMilliseconds(MaxBufferTimeMs);
        private readonly Timer _flushTimer;
        private const int MaxBufferSize = 20;
        private const int MaxBufferTimeMs = 500;

        // Track how many fragments are currently in the queue, to avoid expensive .Count calls.
        private int _count;

        public MessageBuffer(IDatabase db, ISubscriber subscriber, ILogger<RedisConversationState> logger, Guid conversationId)
        {
            _db = db;
            _subscriber = subscriber;
            _logger = logger;
            _conversationId = conversationId;
            _flushTimer = new Timer(_ => TriggerFlush(), null, _flushInterval, _flushInterval);
        }

        public async Task AddFragmentAsync(ClientMessageFragment fragment)
        {
            if (_draining) throw new InvalidOperationException("Buffer is draining");

            _buffer.Enqueue(fragment);
            // Capture the new count from Increment
            var newCount = Interlocked.Increment(ref _count);
            // Invariant: newCount reflects the number of enqueued fragments.
            if (fragment.IsFinal || newCount >= MaxBufferSize)
            {
                await TriggerFlushAsync().ConfigureAwait(false);
            }
        }

        // Flush is triggered either by the timer or reaching thresholds.
        private async Task TriggerFlushAsync()
        {
            // Attempt to acquire the flush lock without blocking.
            if (!await _flushLock.WaitAsync(0).ConfigureAwait(false))
            {
                return; // Another flush is in progress.
            }

            try
            {
                await FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                // Invariant: _flushLock is always released.
                _flushLock.Release();
            }
        }

        // Timer callback for flush.
        private void TriggerFlush()
        {
            if (_draining)
            {
                return;
            }
            _ = TriggerFlushAsync();
        }

        private async Task FlushAsync()
        {
            var fragmentsToFlush = new List<ClientMessageFragment>();
            try
            {
                // Dequeue until empty and decrement the counter for each item.
                while (_buffer.TryDequeue(out var fragment))
                {
                    fragmentsToFlush.Add(fragment);
                    Interlocked.Decrement(ref _count);
                }

                if (fragmentsToFlush.Count > 0)
                {
                    // Log before initiating IO operations.
                    _logger.LogInformation("Flushing {Count} fragments for conversation {ConversationId} {MessageId}", fragmentsToFlush.Count, _conversationId, fragmentsToFlush[0].Id);

                    var key = GetBacklogKey(_conversationId);
                    var channel = GetRedisChannelName(_conversationId);
                    var coalescedFragment = CoalesceFragments(fragmentsToFlush);
                    string serialized = JsonSerializer.Serialize(coalescedFragment);

                    // Use WhenAll to send to Redis in parallel.
                    await Task.WhenAll(
                        _db.ListRightPushAsync(key, serialized),
                        _subscriber.PublishAsync(channel, serialized)
                    ).ConfigureAwait(false);

                    // Log after successful IO.
                    _logger.LogInformation("Successfully flushed fragments for conversation {ConversationId} {MessageId}", _conversationId, fragmentsToFlush[0].Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing fragments for conversation {ConversationId}. Re-enqueueing fragments.", _conversationId);

                // On failure, re-enqueue fragments and re-adjust the counter.
                foreach (var fragment in fragmentsToFlush)
                {
                    _buffer.Enqueue(fragment);
                    Interlocked.Increment(ref _count);
                }
                throw;
            }
        }

        // Called once draining is triggered or on disposal.
        // Invariant: By the time DisposeAsync completes, no further flushes are pending.
        public async ValueTask DisposeAsync()
        {
            if (_draining)
            {
                return;
            }

            _draining = true;

            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            await Task.Yield(); // Let any pending timer callback complete

            try
            {
                await TriggerFlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _flushTimer.Dispose();
                _flushLock.Dispose();
            }
        }
    }

    internal static ClientMessageFragment CoalesceFragments(List<ClientMessageFragment> fragments)
    {
        var lastFragment = fragments[^1];
        int count = fragments.Count;
        int totalLength = 0;
        for (int i = 0; i < count; i++)
        {
            totalLength += fragments[i].Text.Length;
        }
        string combined = string.Create(totalLength, fragments, (span, frags) =>
        {
            int pos = 0;
            for (int i = 0; i < frags.Count; i++)
            {
                ReadOnlySpan<char> text = frags[i].Text;
                text.CopyTo(span.Slice(pos));
                pos += text.Length;
            }
        });
        return new ClientMessageFragment(lastFragment.Id, lastFragment.Sender, combined, lastFragment.FragmentId, lastFragment.IsFinal);
    }
}
