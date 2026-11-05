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
/// The publish call happens while the row locks are held. That keeps the code short and the
/// guarantee easy to reason about, at the cost of a long transaction if the broker is slow.
/// </summary>
public sealed class OutboxDispatcher(DbContext db, IOutboxPublisher publisher, OutboxOptions? options = null, TimeProvider? clock = null)
{
    private readonly OutboxOptions _options = options ?? new OutboxOptions();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <returns>How many messages were published in this run.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var batch = await db.Set<OutboxMessage>()
            .FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND failed_at IS NULL
                ORDER BY created_at
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
                message.ProcessedAt = _clock.GetUtcNow();
                message.Error = null;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.Error = ex.Message;
                if (message.Attempts >= _options.MaxAttempts) message.FailedAt = _clock.GetUtcNow();
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return published;
    }
}
