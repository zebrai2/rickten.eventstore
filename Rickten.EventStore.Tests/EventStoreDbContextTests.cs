using Xunit;
using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework;
using Rickten.EventStore.EntityFramework.Entities;
using System;
using System.Linq;

namespace Rickten.EventStore.Tests;

public class EventStoreDbContextTests
{
    private DbContextOptions<EventStoreDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

    [Fact]
    public void CanInsertAndRetrieveEventEntity()
    {
        var options = CreateOptions("InsertRetrieveEvent");
        var entity = new EventEntity
        {
            StreamType = "Order",
            StreamIdentifier = "order-1",
            Version = 1,
            GlobalPosition = 1,
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

    [Fact]
    public void CanUpdateEventEntity()
    {
        var options = CreateOptions("UpdateEvent");
        using (var context = new EventStoreDbContext(options))
        {
            var entity = new EventEntity
            {
                StreamType = "Order",
                StreamIdentifier = "order-2",
                Version = 1,
                GlobalPosition = 2,
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

    [Fact]
    public void CanDeleteEventEntity()
    {
        var options = CreateOptions("DeleteEvent");
        using (var context = new EventStoreDbContext(options))
        {
            var entity = new EventEntity
            {
                StreamType = "Order",
                StreamIdentifier = "order-3",
                Version = 1,
                GlobalPosition = 3,
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

    [Fact]
    public void CanQueryEventsByStreamTypeAndVersion()
    {
        var options = CreateOptions("QueryEvents");
        using (var context = new EventStoreDbContext(options))
        {
            context.Events.AddRange(
                new EventEntity
                {
                    StreamType = "Invoice",
                    StreamIdentifier = "inv-1",
                    Version = 1,
                    GlobalPosition = 10,
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
                    GlobalPosition = 11,
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

    [Fact]
    public void CanInsertAndRetrieveSnapshotEntity()
    {
        var options = CreateOptions("SnapshotTest");
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

    [Fact]
    public void CanInsertAndRetrieveProjectionEntity()
    {
        var options = CreateOptions("ProjectionTest");
        var projection = new ProjectionEntity
        {
            ProjectionKey = "OrderSummary",
            GlobalPosition = 100,
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
