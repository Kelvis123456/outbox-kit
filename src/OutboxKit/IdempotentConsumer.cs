using Microsoft.EntityFrameworkCore;

namespace OutboxKit;

public static class IdempotentConsumer
{
    /// <summary>
    /// Runs <paramref name="handler"/> at most once per (consumer, messageId).
    ///
    /// The inbox row and the handler's own database changes commit in one transaction, so a
    /// crash cannot record the message as handled without its effect, or apply the effect
    /// without recording it. Returns false when the message was already handled.
    ///
    /// The context must not already have an open transaction.
    /// </summary>
    public static async Task<bool> HandleOnceAsync(
        this DbContext db,
        string consumer,
        Guid messageId,
        Func<CancellationToken, Task> handler,
        CancellationToken ct = default,
        TimeProvider? clock = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inbox_messages (consumer, message_id, processed_at) VALUES ({consumer}, {messageId}, {now}) ON CONFLICT DO NOTHING",
            ct);

        if (inserted == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await handler(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
