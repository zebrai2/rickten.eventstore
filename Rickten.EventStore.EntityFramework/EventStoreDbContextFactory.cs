using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Design-time factory for creating EventStoreDbContext instances during migrations.
/// This factory is used by EF Core tools to create the DbContext when adding or applying migrations.
/// </summary>
public class EventStoreDbContextFactory : IDesignTimeDbContextFactory<EventStoreDbContext>
{
    /// <summary>
    /// Creates a new instance of EventStoreDbContext for design-time operations.
    /// </summary>
    /// <param name="args">Command-line arguments (not used).</param>
    /// <returns>A configured EventStoreDbContext instance.</returns>
    public EventStoreDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EventStoreDbContext>();

        // Use SQL Server by default for migrations
        // In production, connection string and provider should be configured via dependency injection
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=EventStore;Trusted_Connection=True;");

        return new EventStoreDbContext(optionsBuilder.Options);
    }
}
