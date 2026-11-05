# outbox-kit

A small .NET library for the transactional outbox pattern on EF Core and PostgreSQL, plus an idempotent consumer. I wrote it after building [transfer-ledger](https://github.com/Kelvis123456/transfer-ledger) and [webhook-inbox](https://github.com/Kelvis123456/webhook-inbox), because both ran into the same problem from opposite ends: how do you change your database and tell another system about it, without one happening and the other not.

It is a short reference implementation, not a replacement for MassTransit or Wolverine. It is about 150 lines, and the tests prove its guarantees against a real PostgreSQL, including a test that kills the process mid-dispatch.

## The problem

```csharp
await db.SaveChangesAsync();          // committed
await broker.PublishAsync(message);   // process dies here: the change exists, nobody was told
```

Swap the two lines and you get the opposite failure: the message goes out for a change that never committed.

## What it does

1. **Write the message with the change.** `db.Enqueue(type, payload)` adds a row to `outbox_messages`. It is saved by the same `SaveChanges`, so the change and the message commit together or not at all.
2. **Publish later.** `OutboxDispatcher.RunOnceAsync()` locks a batch with `FOR UPDATE SKIP LOCKED`, calls your `IOutboxPublisher`, marks the rows processed and commits. Several dispatchers can run at the same time without publishing the same row twice.
3. **Handle once.** `db.HandleOnceAsync(consumer, messageId, handler)` records the message id in `inbox_messages` and runs your handler in one transaction. A redelivered message is skipped.

```csharp
protected override void OnModelCreating(ModelBuilder b) => b.AddOutbox();

// when you change something
order.MarkPaid();
db.Enqueue("order.paid", JsonSerializer.Serialize(new { order.Id }));
await db.SaveChangesAsync();

// in a background loop
var published = await new OutboxDispatcher(db, myPublisher).RunOnceAsync();

// in the consumer
await db.HandleOnceAsync("billing", messageId, async ct => { /* change your own tables */ });
```

## What the tests prove

`dotnet test` needs Docker (it starts `postgres:17-alpine` with Testcontainers). 9 tests:

- a failing save leaves neither the business row nor the message
- three dispatchers running at once publish each of 40 messages exactly once
- a message the broker keeps rejecting is retried, then parked after `MaxAttempts`, and the rest still go out
- the idempotent consumer runs a handler once for the same message, keeps consumers separate, and records nothing when the handler throws
- **the chaos test:** a separate process publishes three messages and is killed with `Environment.FailFast` before the commit. Nothing is marked processed, the broker already has three. A second worker finishes the job. All 20 messages are processed (none lost), the broker received 23 publishes (three duplicates), and a consumer that replays every one of them twice applies each effect exactly once.

The duplicates in that last test are the point. The outbox gives you at-least-once delivery. The inbox is what turns that into one effect per message.

## Limits

- PostgreSQL only. It uses `FOR UPDATE SKIP LOCKED`, `jsonb` and `ON CONFLICT DO NOTHING`.
- The publish call runs while the row locks are held. A slow broker means a long transaction. Fine for modest volume; if you need more, move the lock to a short lease and publish outside the transaction.
- No ordering guarantee once a message fails: later messages can go out before an earlier one is retried.
- No cleanup of processed rows and no metrics. You will want a job that deletes old `outbox_messages` and `inbox_messages`.
- I did not benchmark it, so I make no throughput claim.
- Not published to NuGet. Clone the repo to use it.

Design decisions are in [docs/decisions.md](docs/decisions.md).
