# Decisions

## 2026-11-05: Publish inside the locking transaction

Alternatives: claim rows with a lease (`locked_until`), commit, publish outside the transaction, then mark them processed in a second transaction.
Why not: the lease version needs a recovery path for rows whose lease expired mid-publish, and that is where most outbox bugs live. Publishing inside the transaction means a crash always rolls back to a clean state. The cost is a long transaction when the broker is slow, which I documented as a limit.

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
