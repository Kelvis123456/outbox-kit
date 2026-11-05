using Microsoft.EntityFrameworkCore;
using Npgsql;
using OutboxKit;

namespace ChaosWorker;

/// <summary>One row per publish call. Written on its own connection, so it survives a rollback or a crash.</summary>
public sealed class BrokerLog
{
    public long Id { get; set; }
    public Guid MessageId { get; set; }
}

/// <summary>The business effect a consumer applies once per message.</summary>
public sealed class Delivery
{
    public long Id { get; set; }
    public Guid MessageId { get; set; }
}

public sealed class DemoDb(DbContextOptions<DemoDb> options) : DbContext(options)
{
    public DbSet<BrokerLog> BrokerLog => Set<BrokerLog>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddOutbox();
        modelBuilder.Entity<BrokerLog>(e =>
        {
            e.ToTable("broker_log");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.MessageId).HasColumnName("message_id");
        });
        modelBuilder.Entity<Delivery>(e =>
        {
            e.ToTable("deliveries");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.MessageId).HasColumnName("message_id");
        });
    }

    public static DemoDb Connect(string connectionString) =>
        new(new DbContextOptionsBuilder<DemoDb>().UseNpgsql(connectionString).Options);
}

/// <summary>Stands in for a message broker: records the publish outside the dispatcher's transaction.</summary>
public sealed class BrokerLogPublisher(string connectionString, Action<int>? afterPublish = null, TimeSpan? delay = null) : IOutboxPublisher
{
    private int _count;

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("INSERT INTO broker_log (message_id) VALUES (@id)", conn);
            cmd.Parameters.AddWithValue("id", message.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (delay is { } d) await Task.Delay(d, ct);
        afterPublish?.Invoke(Interlocked.Increment(ref _count));
    }
}
