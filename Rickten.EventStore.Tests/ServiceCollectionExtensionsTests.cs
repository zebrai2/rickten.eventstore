using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rickten.EventStore;
using Rickten.EventStore.EntityFramework;
using Xunit;

namespace Rickten.EventStore.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEventStore_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStore(options =>
        {
            options.UseInMemoryDatabase("TestDb");
        }, typeof(OrderCreatedEvent).Assembly);

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();
        var dbContext = serviceProvider.GetService<EventStoreDbContext>();

        Assert.NotNull(eventStore);
        Assert.NotNull(snapshotStore);
        Assert.NotNull(projectionStore);
        Assert.NotNull(dbContext);
        Assert.IsType<Rickten.EventStore.EntityFramework.EventStore>(eventStore);
        Assert.IsType<SnapshotStore>(snapshotStore);
        Assert.IsType<ProjectionStore>(projectionStore);
    }

    [Fact]
    public void AddEventStore_RegistersWithScopedLifetime()
    {
        var services = new ServiceCollection();

        services.AddEventStore(options =>
        {
            options.UseInMemoryDatabase("TestDb");
        }, typeof(OrderCreatedEvent).Assembly);

        var eventStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventStore));
        var snapshotStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISnapshotStore));
        var projectionStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IProjectionStore));

        Assert.NotNull(eventStoreDescriptor);
        Assert.NotNull(snapshotStoreDescriptor);
        Assert.NotNull(projectionStoreDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, eventStoreDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, snapshotStoreDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, projectionStoreDescriptor.Lifetime);
    }

    [Fact]
    public void AddEventStore_CalledMultipleTimes_DoesNotRegisterDuplicates()
    {
        var services = new ServiceCollection();

        services.AddEventStore(options => options.UseInMemoryDatabase("TestDb1"), typeof(OrderCreatedEvent).Assembly);
        services.AddEventStore(options => options.UseInMemoryDatabase("TestDb2"), typeof(OrderCreatedEvent).Assembly);

        var eventStoreDescriptors = services.Where(d => d.ServiceType == typeof(IEventStore)).ToList();
        var snapshotStoreDescriptors = services.Where(d => d.ServiceType == typeof(ISnapshotStore)).ToList();
        var projectionStoreDescriptors = services.Where(d => d.ServiceType == typeof(IProjectionStore)).ToList();

        Assert.Single(eventStoreDescriptors);
        Assert.Single(snapshotStoreDescriptors);
        Assert.Single(projectionStoreDescriptors);
    }

    [Fact]
    public void AddEventStoreInMemory_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreInMemory("TestDb", typeof(OrderCreatedEvent).Assembly);

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.NotNull(eventStore);
        Assert.NotNull(snapshotStore);
        Assert.NotNull(projectionStore);
    }

    [Fact]
    public void AddEventStoreSqlServer_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreSqlServer("Server=localhost;Database=EventStore;", new[] { typeof(OrderCreatedEvent).Assembly });

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.NotNull(eventStore);
        Assert.NotNull(snapshotStore);
        Assert.NotNull(projectionStore);
    }

    [Fact]
    public async Task AddEventStore_StoresWorkCorrectly()
    {
        var services = new ServiceCollection();
        services.AddEventStoreInMemory(Guid.NewGuid().ToString(), typeof(OrderCreatedEvent).Assembly);

        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var snapshotStore = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
        var projectionStore = scope.ServiceProvider.GetRequiredService<IProjectionStore>();

        var pointer = new StreamPointer(new StreamIdentifier("Order", "1"), 0);
        var appendEvent = new AppendEvent(new OrderCreatedEvent(100), null);

        var result = await eventStore.AppendAsync(pointer, new[] { appendEvent });

        Assert.Single(result);
        Assert.Equal(1, result[0].StreamPointer.Version);
    }

    [Fact]
    public void AddEventStore_ThrowsWhenServicesIsNull()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddEventStore(options => { }, typeof(OrderCreatedEvent).Assembly);
        });
    }

    [Fact]
    public void AddEventStore_ThrowsWhenOptionsActionIsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddEventStore(null!, typeof(OrderCreatedEvent).Assembly);
        });
    }

    [Fact]
    public void AddEventStoreInMemory_ThrowsWhenDatabaseNameIsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddEventStoreInMemory(null!, typeof(OrderCreatedEvent).Assembly);
        });
    }

    [Fact]
    public void AddEventStoreSqlServer_ThrowsWhenConnectionStringIsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddEventStoreSqlServer(null!, Array.Empty<Assembly>());
        });
    }

    [Fact]
    public void AddEventStore_ThrowsWhenNoAssembliesProvided()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddEventStore(options => options.UseInMemoryDatabase("TestDb"));
        });

        Assert.Contains("At least one assembly must be provided", exception.Message);
        Assert.Contains("type metadata registration", exception.Message);
    }

    [Fact]
    public void AddEventStore_ThrowsWhenEmptyAssemblyArrayProvided()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddEventStore(options => options.UseInMemoryDatabase("TestDb"), Array.Empty<Assembly>());
        });

        Assert.Contains("At least one assembly must be provided", exception.Message);
    }

    [Fact]
    public void AddEventStoreInMemory_ThrowsWhenNoAssembliesProvided()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddEventStoreInMemory("TestDb");
        });

        Assert.Contains("At least one assembly must be provided", exception.Message);
    }

    [Fact]
    public void AddEventStoreSqlServer_ThrowsWhenNoAssembliesProvided()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            services.AddEventStoreSqlServer("Server=localhost;Database=EventStore;", Array.Empty<Assembly>());
        });

        Assert.Contains("At least one assembly must be provided", exception.Message);
    }

    [Fact]
    public void AddEventStore_WithMarkerType_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStore<OrderCreatedEvent>(options =>
        {
            options.UseInMemoryDatabase("TestDb");
        });

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.NotNull(eventStore);
        Assert.NotNull(snapshotStore);
        Assert.NotNull(projectionStore);
    }

    [Fact]
    public void AddEventStore_WithTwoMarkerTypes_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStore<OrderCreatedEvent, OrderUpdatedEvent>(options =>
        {
            options.UseInMemoryDatabase("TestDb");
        });

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
    }

    [Fact]
    public void AddEventStore_WithThreeMarkerTypes_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStore<OrderCreatedEvent, OrderUpdatedEvent, OrderDeletedEvent>(options =>
        {
            options.UseInMemoryDatabase("TestDb");
        });

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
    }

    [Fact]
    public void AddEventStoreInMemory_WithMarkerType_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreInMemory<OrderCreatedEvent>("TestDb");

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.NotNull(eventStore);
        Assert.NotNull(snapshotStore);
        Assert.NotNull(projectionStore);
    }

    [Fact]
    public void AddEventStoreInMemory_WithTwoMarkerTypes_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreInMemory<OrderCreatedEvent, OrderUpdatedEvent>("TestDb");

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
    }

    [Fact]
    public void AddEventStoreInMemory_WithThreeMarkerTypes_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreInMemory<OrderCreatedEvent, OrderUpdatedEvent, OrderDeletedEvent>("TestDb");

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
    }

    [Fact]
    public void AddEventStoreSqlServer_WithMarkerType_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreSqlServer<OrderCreatedEvent>("Server=localhost;Database=EventStore;");

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.NotNull(eventStore);
        Assert.NotNull(snapshotStore);
        Assert.NotNull(projectionStore);
    }

    [Fact]
    public void AddEventStoreSqlServer_WithTwoMarkerTypes_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreSqlServer<OrderCreatedEvent, OrderUpdatedEvent>("Server=localhost;Database=EventStore;");

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
    }

    [Fact]
    public void AddEventStoreSqlServer_WithThreeMarkerTypes_RegistersAllServices()
    {
        var services = new ServiceCollection();

        services.AddEventStoreSqlServer<OrderCreatedEvent, OrderUpdatedEvent, OrderDeletedEvent>("Server=localhost;Database=EventStore;");

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        Assert.NotNull(eventStore);
    }

    [Event("Order", "order-created", 1)]
    public record OrderCreatedEvent(decimal Amount);

    [Event("Order", "order-updated", 1)]
    public record OrderUpdatedEvent(decimal Amount);

    [Event("Order", "order-deleted", 1)]
    public record OrderDeletedEvent(string Reason);
}

