# Decisions

## 2026-11-05: Publish inside the locking transaction

Alternatives: claim rows with a lease (`locked_until`), commit, publish outside the transaction, then mark them processed in a second transaction.
Why not: the lease version needs a recovery path for rows whose lease expired mid-publish, and that is where most outbox bugs live. Publishing inside the transaction means a crash before the commit rolls back every mark made in that run, so the rows are simply pending again. What is not undone is the publishes that already happened, which is why the chaos test sees duplicates. The cost is a long transaction when the broker is slow, which I documented as a limit.

## 2026-11-05: At-least-once plus an idempotent consumer, not "exactly once"

Alternatives: try to make the publish and the commit atomic with two-phase commit.
Why not: no broker I would use supports it. I would rather state the real guarantee and prove it: the chaos test shows the duplicates and shows the inbox absorbing them.

## 2026-11-05: Kill the process for real in the chaos test

Alternatives: throw an exception inside the publisher.
Why not: an exception lets EF roll back cleanly, so it proves nothing about a crash. The test runs a second process and ends it with `Environment.FailFast`, which skips `finally` blocks.

## 2026-11-05: The broker stand-in writes on its own connection

If it wrote inside the dispatcher's transaction, a rollback would also erase the "publish", and the test could not show a duplicate. A separate autocommit connection behaves like a real network call.

## 2026-11-05: Park poison messages instead of retrying forever

After `MaxAttempts` a row gets `FailedAt` and the dispatcher skips it. One bad payload cannot starve the queue. Looking at parked rows and replaying them is left to the user.

## 2026-11-05: Postgres only

`SKIP LOCKED`, `jsonb` and `ON CONFLICT` keep the code short. Supporting SQL Server too would roughly double the SQL and the tests, and I only use Postgres for this kind of work.

## 2026-11-05: No ordering guarantee

Alternatives: process one message at a time in `created_at` order, or add a per-aggregate sequence.
Why not: `created_at` is stamped at `Enqueue`, not at commit, so commit order can differ from it. Parallel dispatchers and retried rows reorder things further. Real ordering needs a single dispatcher or a sequence the consumer checks, and I did not want to add either to a library this size. The README says so plainly.

## 2026-11-05: The dispatcher never uses the caller's change tracker

It reads the batch with `AsNoTracking` and writes each result with `ExecuteUpdate`. With tracking, a long-lived context kept every processed message, did not refresh rows it already held, and `SaveChanges` also saved whatever unrelated changes the caller had pending, inside the dispatcher's transaction. The cost is one UPDATE per message instead of one batched save.

## 2026-11-05: `RunOnceAsync` returns a result, not a count

A bare count made "nothing pending" and "the whole batch failed" both 0, so `while (RunOnceAsync() > 0)` stopped with retryable rows left. `DispatchResult` has `Published`, `Failed` and `Claimed`; drain loops use `Claimed`.
