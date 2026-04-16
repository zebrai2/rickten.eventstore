using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.EntityFramework.Entities;
using System;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for EventStoreDbContext using SQLite in-memory database.
/// SQLite in-memory provides REAL database behavior (constraints, transactions, indexes)
/// unlike EF's InMemoryDatabase which doesn't enforce constraints.
/// </summary>
public class EventStoreDbContextTests
{
    private (SqliteConnection Connection, DbContextOptions<EventStoreDbContext> Options) CreateOptions()
    {
        // Create in-memory SQLite database with shared connection
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseSqlite(connection)  // Real database with constraint enforcement!
            .Options;

        // Create the database schema
        using var context = new EventStoreDbContext(options);
        context.Database.EnsureCreated();

        return (connection, options);
    }

    [Fact]
    public void CanInsertAndRetrieveEventEntity()
    {
        var (connection, options) = CreateOptions();
        using (connection)
        {
            var entity = new EventEntity
            {
                StreamType = "Order",
                StreamIdentifier = "order-1",
                Version = 1,
                EventType = "OrderCreated",
                EventData = "{\"amount\":100}",
                Metadata = "{}",
                CreatedAt = DateTime.UtcNow
            };

            using (var context = new EventStoreDbContext(options))
            {
                context.Events.Add(entity);
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                var loaded = context.Events.Single();
                Assert.Equal("Order", loaded.StreamType);
                Assert.Equal("order-1", loaded.StreamIdentifier);
                Assert.Equal("OrderCreated", loaded.EventType);
            }
        }
    }

    [Fact]
    public void CanUpdateEventEntity()
    {
        var (connection, options) = CreateOptions();
        using (connection)
        {
            using (var context = new EventStoreDbContext(options))
            {
                var entity = new EventEntity
                {
                    StreamType = "Order",
                    StreamIdentifier = "order-2",
                    Version = 1,
                    EventType = "OrderCreated",
                    EventData = "{}",
                    Metadata = "{}",
                    CreatedAt = DateTime.UtcNow
                };
                context.Events.Add(entity);
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                var entity = context.Events.Single(e => e.StreamIdentifier == "order-2");
                entity.EventType = "OrderUpdated";
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                var entity = context.Events.Single(e => e.StreamIdentifier == "order-2");
                Assert.Equal("OrderUpdated", entity.EventType);
            }
        }
    }

    [Fact]
    public void CanDeleteEventEntity()
    {
        var (connection, options) = CreateOptions();
        using (connection)
        {
            using (var context = new EventStoreDbContext(options))
            {
                var entity = new EventEntity
                {
                    StreamType = "Order",
                    StreamIdentifier = "order-3",
                    Version = 1,
                    EventType = "OrderCreated",
                    EventData = "{}",
                    Metadata = "{}",
                    CreatedAt = DateTime.UtcNow
                };
                context.Events.Add(entity);
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                var entity = context.Events.Single(e => e.StreamIdentifier == "order-3");
                context.Events.Remove(entity);
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                Assert.Empty(context.Events.Where(e => e.StreamIdentifier == "order-3"));
            }
        }
    }

    [Fact]
    public void CanQueryEventsByStreamTypeAndVersion()
    {
        var (connection, options) = CreateOptions();
        using (connection)
        {
            using (var context = new EventStoreDbContext(options))
            {
                context.Events.AddRange(
                    new EventEntity
                    {
                        StreamType = "Invoice",
                        StreamIdentifier = "inv-1",
                        Version = 1,
                        EventType = "InvoiceCreated",
                        EventData = "{}",
                        Metadata = "{}",
                        CreatedAt = DateTime.UtcNow
                    },
                    new EventEntity
                    {
                        StreamType = "Invoice",
                        StreamIdentifier = "inv-1",
                        Version = 2,
                        EventType = "InvoicePaid",
                        EventData = "{}",
                        Metadata = "{}",
                        CreatedAt = DateTime.UtcNow
                    }
                );
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                var events = context.Events
                    .Where(e => e.StreamType == "Invoice" && e.StreamIdentifier == "inv-1")
                    .OrderBy(e => e.Version)
                    .ToList();
                Assert.Equal(2, events.Count);
                Assert.Equal("InvoiceCreated", events[0].EventType);
                Assert.Equal("InvoicePaid", events[1].EventType);
            }
        }
    }

    [Fact]
    public void CanInsertAndRetrieveSnapshotEntity()
    {
        var (connection, options) = CreateOptions();
        using (connection)
        {
            var snapshot = new SnapshotEntity
            {
                StreamType = "Order",
                StreamIdentifier = "order-1",
                Version = 2,
                StateType = "Order.State.v1",
                State = "{\"status\":\"shipped\"}",
                CreatedAt = DateTime.UtcNow
            };

            using (var context = new EventStoreDbContext(options))
            {
                context.Snapshots.Add(snapshot);
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                var loaded = context.Snapshots.Single();
                Assert.Equal("Order", loaded.StreamType);
                Assert.Equal(2, loaded.Version);
            }
        }
    }

    [Fact]
    public void CanInsertAndRetrieveProjectionEntity()
    {
        var (connection, options) = CreateOptions();
        using (connection)
        {
            var projection = new ProjectionEntity
            {
                ProjectionKey = "OrderSummary",
                GlobalPosition = 100,
                StateType = "TestProjection.State",
                State = "{\"count\":5}",
                UpdatedAt = DateTime.UtcNow
            };

            using (var context = new EventStoreDbContext(options))
            {
                context.Projections.Add(projection);
                context.SaveChanges();
            }

            using (var context = new EventStoreDbContext(options))
            {
                var loaded = context.Projections.Single();
                Assert.Equal("OrderSummary", loaded.ProjectionKey);
                Assert.Equal(100, loaded.GlobalPosition);
            }
        }
    }

    [Fact]
    public void UniqueConstraint_PreventsDuplicateVersions()
    {
        var (connection, options) = CreateOptions();
        using (connection)
        {
            using var context = new EventStoreDbContext(options);

            // Insert first event
            context.Events.Add(new EventEntity
            {
                StreamType = "Order",
                StreamIdentifier = "order-constraint",
                Version = 1,
                EventType = "OrderCreated",
                EventData = "{}",
                Metadata = "{}",
                CreatedAt = DateTime.UtcNow
            });
            context.SaveChanges();

            // Try to insert duplicate version - should fail with unique constraint violation
            context.Events.Add(new EventEntity
            {
                StreamType = "Order",
                StreamIdentifier = "order-constraint",
                Version = 1,  // Duplicate!
                EventType = "OrderUpdated",
                EventData = "{}",
                Metadata = "{}",
                CreatedAt = DateTime.UtcNow
            });

            // SQLite will throw DbUpdateException, EF InMemory would silently allow this!
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }
    }
}
