using Microsoft.EntityFrameworkCore;

namespace OutboxKit;

public interface IOutboxPublisher
{
    /// <summary>Send the message to the broker. Throw to leave it for a retry.</summary>
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

public sealed record OutboxOptions
{
    public int BatchSize { get; init; } = 50;

    /// <summary>After this many failed publishes a message is parked (FailedAt) and no longer retried.</summary>
    public int MaxAttempts { get; init; } = 5;
}

/// <summary>
/// Publishes pending messages with at-least-once delivery.
///
/// One run is one transaction: it locks a batch with FOR UPDATE SKIP LOCKED, publishes each
/// row, marks it processed and commits. Several dispatchers can run at once because each one
/// skips rows another has locked. If the process dies after publishing but before the commit,
/// the transaction rolls back and the rows are published again by the next run. That duplicate
/// is the price of at-least-once, so consumers must be idempotent (see IdempotentConsumer).
///
/// RunOnceAsync opens its own transaction, so it throws InvalidOperationException if the context
/// already has one open. It never touches the context's change tracker: pending changes on it are
/// neither saved nor discarded.
///
/// The publish call happens while the row locks are held. That keeps the code short and the
/// guarantee easy to reason about, at the cost of a long transaction if the broker is slow.
/// </summary>
public sealed class OutboxDispatcher(DbContext db, IOutboxPublisher publisher, OutboxOptions? options = null, TimeProvider? clock = null)
{
    private readonly OutboxOptions _options = Validate(options ?? new OutboxOptions());
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private static OutboxOptions Validate(OutboxOptions o)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(o.BatchSize, 1, nameof(o.BatchSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(o.MaxAttempts, 1, nameof(o.MaxAttempts));
        return o;
    }

    /// <returns>How many messages were published in this run.</returns>
    public Task<int> RunOnceAsync(CancellationToken ct = default) =>
        // Goes through the execution strategy so a context configured with EnableRetryOnFailure works.
        // On a transient failure the whole run repeats, which can publish a row again (at-least-once).
        db.Database.CreateExecutionStrategy().ExecuteAsync(this, static (self, token) => self.RunAttemptAsync(token), ct);

    private async Task<int> RunAttemptAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // AsNoTracking plus per-row ExecuteUpdate: the dispatcher never touches the caller's change
        // tracker, so it cannot save their pending changes or trust a stale tracked copy of a row.
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw(OutboxSql.PendingBatch(db), _options.BatchSize)
            .AsNoTracking()
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in batch)
        {
            var id = message.Id;
            Exception? failure = null;
            try
            {
                await publisher.PublishAsync(message, ct);
            }
            // Only our own cancellation aborts the run. A cancellation the publisher raises (an HttpClient
            // timeout, say) is an ordinary failed attempt, otherwise the row would never count it.
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failure = ex;
            }

            if (failure is null)
            {
                var now = _clock.GetUtcNow();
                await db.Set<OutboxMessage>().Where(m => m.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.ProcessedAt, now)
                    .SetProperty(m => m.Error, (string?)null), ct);
                published++;
            }
            else
            {
                var attempts = message.Attempts + 1; // the row is locked, so the value we read is current
                var error = failure.Message;
                DateTimeOffset? failedAt = attempts >= _options.MaxAttempts ? _clock.GetUtcNow() : null;
                await db.Set<OutboxMessage>().Where(m => m.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Attempts, attempts)
                    .SetProperty(m => m.Error, error)
                    .SetProperty(m => m.FailedAt, failedAt), ct);
            }
        }

        await tx.CommitAsync(ct);
        return published;
    }
}
