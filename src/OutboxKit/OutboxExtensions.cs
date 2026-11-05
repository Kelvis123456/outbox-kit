using Microsoft.EntityFrameworkCore;

namespace OutboxKit;

public static class OutboxExtensions
{
    internal const string OutboxTable = "outbox_messages";
    internal const string InboxTable = "inbox_messages";

    /// <summary>Call from OnModelCreating to add the outbox and inbox tables.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable(OutboxTable);
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.Type).HasColumnName("type").IsRequired();
            e.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            e.Property(m => m.CreatedAt).HasColumnName("created_at");
            e.Property(m => m.ProcessedAt).HasColumnName("processed_at");
            e.Property(m => m.Attempts).HasColumnName("attempts");
            e.Property(m => m.Error).HasColumnName("error");
            e.Property(m => m.FailedAt).HasColumnName("failed_at");
            // The dispatcher only ever reads pending rows, so index only those.
            e.HasIndex(m => m.CreatedAt).HasFilter("processed_at IS NULL AND failed_at IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.ToTable(InboxTable);
            e.HasKey(m => new { m.Consumer, m.MessageId });
            e.Property(m => m.Consumer).HasColumnName("consumer");
            e.Property(m => m.MessageId).HasColumnName("message_id");
            e.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        });

        return modelBuilder;
    }

    /// <summary>
    /// Stages a message. It is saved by the caller's next SaveChanges, in the same
    /// transaction as the business change, which is the whole point of the pattern.
    /// </summary>
    public static OutboxMessage Enqueue(this DbContext db, string type, string payload, TimeProvider? clock = null)
    {
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Payload = payload,
            CreatedAt = (clock ?? TimeProvider.System).GetUtcNow(),
        };
        db.Set<OutboxMessage>().Add(message);
        return message;
    }
}
