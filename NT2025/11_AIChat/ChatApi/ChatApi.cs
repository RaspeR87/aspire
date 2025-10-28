using Microsoft.EntityFrameworkCore;

public static class ChatExtensions
{
    public static void MapChatApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat");

        group.MapGet("/", (AppDbContext db) =>
        {
            return db.Conversations.ToListAsync();
        });

        group.MapGet("/{id}", async (Guid id, AppDbContext db) =>
        {
            var conversation = await db.Conversations.FindAsync(id);

            if (conversation is null)
            {
                return Results.NotFound();
            }

            var clientMessages = from m in conversation.Messages
                                 select new ClientMessage(m.Id, m.Role, m.Text);

            return Results.Ok(clientMessages);
        });

        group.MapHub<ChatHub>("/stream", o => o.AllowStatefulReconnects = true);

        group.MapPost("/", async (NewConversation newConversation, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(newConversation.Name))
            {
                return Results.BadRequest();
            }

            var conversation = new Conversation
            {
                Id = Guid.CreateVersion7(),
                Name = newConversation.Name,
                Messages = []
            };

            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();

            return Results.Created($"/api/chats/{conversation.Id}", conversation);
        });

        group.MapPost("/{id}", async (Guid id, AppDbContext db, Prompt prompt, CancellationToken token, ChatStreamingCoordinator streaming) =>
        {
            // Fire and forget
            await streaming.AddStreamingMessage(id, prompt.Text);

            return Results.Ok();
        });

        group.MapPost("/{id}/cancel", async (Guid id, ICancellationManager cancellationManager) =>
        {
            await cancellationManager.CancelAsync(id);

            return Results.Ok();
        });

        group.MapDelete("/{id}", async (Guid id, AppDbContext db) =>
        {
            var conversation = await db.Conversations.FindAsync(id);

            if (conversation is null)
            {
                return Results.NotFound();
            }

            db.Conversations.Remove(conversation);
            await db.SaveChangesAsync();

            return Results.Ok();
        });
    }
}

public record Prompt(string Text);

public record NewConversation(string Name);

public record ClientMessage(Guid Id, string Sender, string Text);

public record ClientMessageFragment(Guid Id, string Sender, string Text, Guid FragmentId, bool IsFinal = false);

public record StreamContext(Guid? LastMessageId, Guid? LastFragmentId);