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
    /// The context must not already have an open transaction (InvalidOperationException), and must have no pending changes:
    /// the final SaveChanges would otherwise commit them along with the handler's, and a skipped
    /// duplicate would leave them queued. Both cases throw InvalidOperationException.
    ///
    /// If the handler (or the save) throws, the transaction rolls back and every entity the
    /// handler added, modified or deleted is reverted in the change tracker, so calling this
    /// again on the same context applies the effect once, not twice.
    /// </summary>
    public static async Task<bool> HandleOnceAsync(
        this DbContext db,
        string consumer,
        Guid messageId,
        Func<CancellationToken, Task> handler,
        CancellationToken ct = default,
        TimeProvider? clock = null)
    {
        // Through the execution strategy so EnableRetryOnFailure works. After a transient failure the
        // handler runs again, which is safe because every failed attempt rolls back and is reverted.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            (db, consumer, messageId, handler, clock),
            static (s, token) => RunAsync(s.db, s.consumer, s.messageId, s.handler, s.clock, token),
            ct);
    }

    private static async Task<bool> RunAsync(
        DbContext db, string consumer, Guid messageId, Func<CancellationToken, Task> handler, TimeProvider? clock, CancellationToken ct)
    {
        if (db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("The context has pending changes. Save or discard them before HandleOnceAsync.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var inserted = await db.Database.ExecuteSqlRawAsync(OutboxSql.InboxInsert(db), [consumer, messageId, now], ct);

        if (inserted == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        try
        {
            await handler(ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            DiscardChanges(db); // the context started clean, so everything pending came from the handler
            throw;
        }

        return true;
    }

    private static void DiscardChanges(DbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }
}
