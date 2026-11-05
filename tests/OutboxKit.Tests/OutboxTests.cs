using ChaosWorker;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OutboxKit.Tests;

[Collection("postgres")]
public sealed class OutboxTests(PostgresFixture pg)
{
    private static async Task SeedAsync(string cs, int count)
    {
        await using var db = DemoDb.Connect(cs);
        for (var i = 0; i < count; i++) db.Enqueue("demo.created", $$"""{"n":{{i}}}""");
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Enqueue_is_saved_in_the_same_transaction_as_the_business_change()
    {
        var cs = await pg.NewDatabaseAsync();
        await using var db = DemoDb.Connect(cs);

        db.Deliveries.Add(new Delivery { MessageId = Guid.NewGuid() });
        db.Enqueue("demo.created", """{"ok":true}""");
        await db.SaveChangesAsync();

        await using var check = DemoDb.Connect(cs);
        Assert.Equal(1, await check.Deliveries.CountAsync());
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task A_failing_save_leaves_neither_the_business_row_nor_the_message()
    {
        var cs = await pg.NewDatabaseAsync();
        await using var db = DemoDb.Connect(cs);

        db.Deliveries.Add(new Delivery { MessageId = Guid.NewGuid() });
        db.Enqueue("demo.created", "this is not json"); // jsonb rejects it, so the whole SaveChanges fails

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var check = DemoDb.Connect(cs);
        Assert.Equal(0, await check.Deliveries.CountAsync());
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task Dispatcher_publishes_pending_messages_in_batches_and_marks_them_processed()
    {
        var cs = await pg.NewDatabaseAsync();
        await SeedAsync(cs, 25);

        await using var db = DemoDb.Connect(cs);
        var dispatcher = new OutboxDispatcher(db, new BrokerLogPublisher(cs), new OutboxOptions { BatchSize = 10 });

        Assert.Equal(10, await dispatcher.RunOnceAsync());
        Assert.Equal(10, await dispatcher.RunOnceAsync());
        Assert.Equal(5, await dispatcher.RunOnceAsync());
        Assert.Equal(0, await dispatcher.RunOnceAsync());

        await using var check = DemoDb.Connect(cs);
        Assert.Equal(25, await check.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null));
        Assert.Equal(25, await check.BrokerLog.CountAsync());
    }

    [Fact]
    public async Task Two_dispatchers_running_at_once_never_publish_the_same_message()
    {
        var cs = await pg.NewDatabaseAsync();
        await SeedAsync(cs, 40);

        async Task Worker()
        {
            await using var db = DemoDb.Connect(cs);
            // The delay keeps the row locks held long enough for the two runs to overlap.
            var dispatcher = new OutboxDispatcher(db, new BrokerLogPublisher(cs, delay: TimeSpan.FromMilliseconds(20)), new OutboxOptions { BatchSize = 10 });
            while (await dispatcher.RunOnceAsync() > 0) { }
        }

        await Task.WhenAll(Worker(), Worker(), Worker());

        await using var check = DemoDb.Connect(cs);
        var perMessage = await check.BrokerLog.GroupBy(b => b.MessageId).Select(g => g.Count()).ToListAsync();
        Assert.Equal(40, perMessage.Count);
        Assert.All(perMessage, count => Assert.Equal(1, count));
        Assert.Equal(40, await check.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null));
    }

    private sealed class PoisonPublisher(Guid poison, BrokerLogPublisher inner) : IOutboxPublisher
    {
        public Task PublishAsync(OutboxMessage message, CancellationToken ct) =>
            message.Id == poison ? throw new InvalidOperationException("broker rejected it") : inner.PublishAsync(message, ct);
    }

    [Fact]
    public async Task A_poison_message_is_retried_then_parked_and_does_not_block_the_others()
    {
        var cs = await pg.NewDatabaseAsync();
        await SeedAsync(cs, 5);
        Guid poison;
        await using (var seedCheck = DemoDb.Connect(cs))
            poison = (await seedCheck.Set<OutboxMessage>().OrderBy(m => m.CreatedAt).Skip(2).FirstAsync()).Id;

        await using var db = DemoDb.Connect(cs);
        var dispatcher = new OutboxDispatcher(db, new PoisonPublisher(poison, new BrokerLogPublisher(cs)), new OutboxOptions { BatchSize = 10, MaxAttempts = 3 });

        Assert.Equal(4, await dispatcher.RunOnceAsync()); // the other four go out on the first run
        await dispatcher.RunOnceAsync();
        await dispatcher.RunOnceAsync();
        Assert.Equal(0, await dispatcher.RunOnceAsync()); // parked after 3 attempts, so nothing is left to try

        await using var check = DemoDb.Connect(cs);
        var parked = await check.Set<OutboxMessage>().SingleAsync(m => m.Id == poison);
        Assert.Equal(3, parked.Attempts);
        Assert.NotNull(parked.FailedAt);
        Assert.Null(parked.ProcessedAt);
        Assert.Equal("broker rejected it", parked.Error);
        Assert.Equal(4, await check.BrokerLog.CountAsync());
    }

    private sealed class TimeoutPublisher(Guid victim, BrokerLogPublisher inner) : IOutboxPublisher
    {
        // HttpClient.Timeout surfaces as TaskCanceledException even though nobody cancelled our token.
        public Task PublishAsync(OutboxMessage message, CancellationToken ct) =>
            message.Id == victim ? throw new TaskCanceledException("the broker call timed out") : inner.PublishAsync(message, ct);
    }

    [Fact]
    public async Task A_publisher_timeout_counts_as_a_failed_attempt_and_does_not_stall_the_queue()
    {
        var cs = await pg.NewDatabaseAsync();
        await SeedAsync(cs, 5);
        Guid victim;
        await using (var seedCheck = DemoDb.Connect(cs))
            victim = (await seedCheck.Set<OutboxMessage>().OrderBy(m => m.CreatedAt).Skip(2).FirstAsync()).Id;

        await using var db = DemoDb.Connect(cs);
        var dispatcher = new OutboxDispatcher(db, new TimeoutPublisher(victim, new BrokerLogPublisher(cs)), new OutboxOptions { BatchSize = 10, MaxAttempts = 3 });

        Assert.Equal(4, await dispatcher.RunOnceAsync());

        await using var check = DemoDb.Connect(cs);
        var stuck = await check.Set<OutboxMessage>().SingleAsync(m => m.Id == victim);
        Assert.Equal(1, stuck.Attempts);
        Assert.Equal(4, await check.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null));
        Assert.Equal(4, await check.BrokerLog.CountAsync()); // and nobody is published twice
    }

    [Fact]
    public async Task Cancelling_the_run_itself_still_rolls_back_and_throws()
    {
        var cs = await pg.NewDatabaseAsync();
        await SeedAsync(cs, 3);
        using var cts = new CancellationTokenSource();

        await using var db = DemoDb.Connect(cs);
        var publisher = new BrokerLogPublisher(cs, afterPublish: n => { if (n == 2) cts.Cancel(); }, delay: TimeSpan.FromMilliseconds(5));
        var dispatcher = new OutboxDispatcher(db, publisher);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.RunOnceAsync(cts.Token));

        await using var check = DemoDb.Connect(cs);
        Assert.Equal(0, await check.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null || m.Attempts > 0));
    }

    [Fact]
    public async Task Idempotent_consumer_runs_the_handler_once_per_message_even_when_called_twice()
    {
        var cs = await pg.NewDatabaseAsync();
        await using var db = DemoDb.Connect(cs);
        var id = Guid.NewGuid();

        Task<bool> Handle() => db.HandleOnceAsync("billing", id, _ =>
        {
            db.Deliveries.Add(new Delivery { MessageId = id });
            return Task.CompletedTask;
        });

        Assert.True(await Handle());
        Assert.False(await Handle());

        await using var check = DemoDb.Connect(cs);
        Assert.Equal(1, await check.Deliveries.CountAsync());
    }

    [Fact]
    public async Task Idempotent_consumer_keeps_consumers_separate()
    {
        var cs = await pg.NewDatabaseAsync();
        await using var db = DemoDb.Connect(cs);
        var id = Guid.NewGuid();

        Assert.True(await db.HandleOnceAsync("billing", id, _ => Task.CompletedTask));
        Assert.True(await db.HandleOnceAsync("emails", id, _ => Task.CompletedTask));
        Assert.False(await db.HandleOnceAsync("billing", id, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task A_handler_that_throws_records_nothing_so_the_message_can_be_retried()
    {
        var cs = await pg.NewDatabaseAsync();
        var id = Guid.NewGuid();

        await using (var db = DemoDb.Connect(cs))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                db.HandleOnceAsync("billing", id, _ => throw new InvalidOperationException("boom")));
        }

        await using var retry = DemoDb.Connect(cs);
        Assert.Equal(0, await retry.Set<InboxMessage>().CountAsync());
        Assert.True(await retry.HandleOnceAsync("billing", id, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Killing_the_process_between_publish_and_commit_loses_nothing_and_applies_each_effect_once()
    {
        var cs = await pg.NewDatabaseAsync();
        const int total = 20;
        await SeedAsync(cs, total);

        var crash = await RunWorkerAsync(cs, "crash");
        Assert.NotEqual(0, crash); // FailFast really killed it

        await using (var mid = DemoDb.Connect(cs))
        {
            // The commit never happened, so nothing is marked processed...
            Assert.Equal(0, await mid.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null));
            // ...but the broker already received three of them.
            Assert.Equal(3, await mid.BrokerLog.CountAsync());
        }

        Assert.Equal(0, await RunWorkerAsync(cs, "drain"));

        await using var db = DemoDb.Connect(cs);
        Assert.Equal(total, await db.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null)); // nothing lost
        var published = await db.BrokerLog.CountAsync();
        Assert.Equal(total + 3, published); // at-least-once: the three sent before the crash went out again

        // A consumer that reads the broker, including a redelivery of everything, applies each effect once.
        var broker = await db.BrokerLog.Select(b => b.MessageId).ToListAsync();
        foreach (var id in broker.Concat(broker))
        {
            await using var consumer = DemoDb.Connect(cs);
            await consumer.HandleOnceAsync("deliveries", id, _ =>
            {
                consumer.Deliveries.Add(new Delivery { MessageId = id });
                return Task.CompletedTask;
            });
        }

        await using var final = DemoDb.Connect(cs);
        var perMessage = await final.Deliveries.GroupBy(d => d.MessageId).Select(g => g.Count()).ToListAsync();
        Assert.Equal(total, perMessage.Count);
        Assert.All(perMessage, count => Assert.Equal(1, count));
    }

    private static async Task<int> RunWorkerAsync(string connectionString, string mode)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "ChaosWorker.dll");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add(connectionString);
        psi.ArgumentList.Add(mode);

        using var process = System.Diagnostics.Process.Start(psi)!;
        // Drain the pipes so a full buffer cannot block the child.
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return process.ExitCode;
    }
}
