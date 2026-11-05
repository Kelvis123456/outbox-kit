using ChaosWorker;
using OutboxKit;

// Usage: ChaosWorker <connection string> <crash|drain>
// crash: kills the process after the 3rd publish, before the dispatcher commits.
// drain: runs the dispatcher until nothing is pending.
var connectionString = args[0];
var mode = args[1];

await using var db = DemoDb.Connect(connectionString);

if (mode == "crash")
{
    var publisher = new BrokerLogPublisher(connectionString, afterPublish: n =>
    {
        // FailFast skips finally blocks and the transaction's rollback, like a power cut would.
        if (n == 3) Environment.FailFast("chaos: killed between publish and commit");
    });
    await new OutboxDispatcher(db, publisher, new OutboxOptions { BatchSize = 10 }).RunOnceAsync();
}
else
{
    var dispatcher = new OutboxDispatcher(db, new BrokerLogPublisher(connectionString), new OutboxOptions { BatchSize = 10 });
    while ((await dispatcher.RunOnceAsync()).Claimed > 0) { }
}
