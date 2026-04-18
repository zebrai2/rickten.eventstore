using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore;
using System;
using Microsoft.Data.Sqlite;

namespace Rickten.EventStore.Tests.Integration;

[Event("TraceTestSqlite", "TestEvent", 1)]
public record TraceTestEventSqlite(string Name);

/// <summary>
/// SQLite integration tests for trace identity metadata.
/// </summary>
public class TraceIdentityIntegrationTestsSqlite : TraceIdentityIntegrationTestsBase, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EventStoreDbContext> _options;

    public TraceIdentityIntegrationTestsSqlite()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new EventStoreDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    protected override string AggregateType => "TraceTestSqlite";
    protected override void SkipIfNotAvailable() { }
    protected override EventStoreDbContext CreateContext() => new EventStoreDbContext(_options);
    protected override object CreateTestEvent(string name) => new TraceTestEventSqlite(name);
}
