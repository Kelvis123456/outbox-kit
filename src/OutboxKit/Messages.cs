namespace OutboxKit;

/// <summary>A message written in the same transaction as the business change, published later.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    /// <summary>JSON text. It is stored as jsonb, so Postgres normalizes it: key order and whitespace change, duplicate keys collapse.</summary>
    public string Payload { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }

    /// <summary>Set when Attempts reaches the limit. The dispatcher skips these rows.</summary>
    public DateTimeOffset? FailedAt { get; set; }
}

/// <summary>Records that a consumer already handled a message id.</summary>
public sealed class InboxMessage
{
    public string Consumer { get; set; } = "";
    public Guid MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
