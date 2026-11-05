using ChaosWorker;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OutboxKit.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A fresh, empty database per test, so tests cannot see each other's rows.</summary>
    public async Task<string> NewDatabaseAsync()
    {
        var cs = await NewEmptyDatabaseAsync();
        await using var db = DemoDb.Connect(cs);
        await db.Database.EnsureCreatedAsync();
        return cs;
    }

    /// <summary>A fresh database with no tables, for tests that bring their own model.</summary>
    public async Task<string> NewEmptyDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
