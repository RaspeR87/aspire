using Microsoft.AspNetCore.SignalR;

public class ChatHub : Hub
{
    public IAsyncEnumerable<ClientMessageFragment> Stream(Guid id, StreamContext streamContext, ChatStreamingCoordinator streaming, CancellationToken token)
    {
        async IAsyncEnumerable<ClientMessageFragment> Stream()
        {
            await foreach (var message in streaming.GetMessageStream(id, streamContext.LastMessageId, streamContext.LastFragmentId).WithCancellation(token))
            {
                yield return message;
            }
        }

        return Stream();
    }
}