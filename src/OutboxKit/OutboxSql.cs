using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutboxKit;

/// <summary>
/// The two raw statements, built from the model so a default schema or renamed columns keep working.
/// Names come from model metadata, never from user input, and are quoted by the provider.
/// </summary>
internal static class OutboxSql
{
    /// <summary>Parameter {0} is the batch size.</summary>
    public static string PendingBatch(DbContext db)
    {
        var (table, col) = Resolve<OutboxMessage>(db);
        return $$"""
            SELECT * FROM {{table}}
            WHERE {{col(nameof(OutboxMessage.ProcessedAt))}} IS NULL AND {{col(nameof(OutboxMessage.FailedAt))}} IS NULL
            ORDER BY {{col(nameof(OutboxMessage.CreatedAt))}}, {{col(nameof(OutboxMessage.Id))}}
            LIMIT {0}
            FOR UPDATE SKIP LOCKED
            """;
    }

    /// <summary>Parameters {0}, {1}, {2} are consumer, message id and processed-at.</summary>
    public static string InboxInsert(DbContext db)
    {
        var (table, col) = Resolve<InboxMessage>(db);
        return $"INSERT INTO {table} ({col(nameof(InboxMessage.Consumer))}, {col(nameof(InboxMessage.MessageId))}, {col(nameof(InboxMessage.ProcessedAt))}) VALUES ({{0}}, {{1}}, {{2}}) ON CONFLICT DO NOTHING";
    }

    private static (string Table, Func<string, string> Column) Resolve<T>(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not in the model. Call modelBuilder.AddOutbox().");
        var name = entity.GetTableName()!;
        var schema = entity.GetSchema();
        var store = StoreObjectIdentifier.Table(name, schema);
        var sql = db.GetService<ISqlGenerationHelper>();
        return (sql.DelimitIdentifier(name, schema), prop => sql.DelimitIdentifier(entity.FindProperty(prop)!.GetColumnName(store)!));
    }
}
