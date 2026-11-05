using Microsoft.EntityFrameworkCore;

namespace OutboxKit.Tests;

public sealed class SchemaDb(DbContextOptions<SchemaDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("messaging");
        modelBuilder.AddOutbox();
    }
}

[Collection("postgres")]
public sealed class SchemaTests(PostgresFixture pg)
{
    private sealed class ListPublisher : IOutboxPublisher
    {
        public List<Guid> Sent { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            Sent.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Dispatcher_and_consumer_work_with_a_custom_default_schema()
    {
        var cs = await pg.NewEmptyDatabaseAsync();
        await using var db = new SchemaDb(new DbContextOptionsBuilder<SchemaDb>().UseNpgsql(cs).Options);
        await db.Database.EnsureCreatedAsync();

        var message = db.Enqueue("demo.created", """{"ok":true}""");
        await db.SaveChangesAsync();

        var publisher = new ListPublisher();
        Assert.Equal(1, await new OutboxDispatcher(db, publisher).RunOnceAsync());
        Assert.Equal([message.Id], publisher.Sent);

        Assert.True(await db.HandleOnceAsync("billing", message.Id, _ => Task.CompletedTask));
        Assert.False(await db.HandleOnceAsync("billing", message.Id, _ => Task.CompletedTask));
    }
}
