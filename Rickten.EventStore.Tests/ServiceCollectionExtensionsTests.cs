using System;
using System.Linq;
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
        });

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
        });

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
    public void AddEventStore_WithCustomLifetime_RegistersCorrectly()
    {
        var services = new ServiceCollection();
        
        services.AddEventStore(
            options => options.UseInMemoryDatabase("TestDb"),
            ServiceLifetime.Transient);

        var eventStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventStore));
        var snapshotStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISnapshotStore));
        var projectionStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IProjectionStore));

        Assert.NotNull(eventStoreDescriptor);
        Assert.NotNull(snapshotStoreDescriptor);
        Assert.NotNull(projectionStoreDescriptor);
        Assert.Equal(ServiceLifetime.Transient, eventStoreDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, snapshotStoreDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Transient, projectionStoreDescriptor.Lifetime);
    }

    [Fact]
    public void AddEventStoreInMemory_RegistersAllServices()
    {
        var services = new ServiceCollection();
        
        services.AddEventStoreInMemory("TestDb");

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
        
        services.AddEventStoreSqlServer("Server=localhost;Database=EventStore;");

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
        services.AddEventStoreInMemory(Guid.NewGuid().ToString());

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
            services.AddEventStore(options => { });
        });
    }

    [Fact]
    public void AddEventStore_ThrowsWhenOptionsActionIsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddEventStore(null!);
        });
    }

    [Fact]
    public void AddEventStoreInMemory_ThrowsWhenDatabaseNameIsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddEventStoreInMemory(null!);
        });
    }

    [Fact]
    public void AddEventStoreSqlServer_ThrowsWhenConnectionStringIsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddEventStoreSqlServer(null!);
        });
    }

    [Fact]
    public void AddEventStoreOnly_RegistersOnlyEventStore()
    {
        var services = new ServiceCollection();

        services.AddEventStoreOnly(options =>
        {
            options.UseInMemoryDatabase("EventsDb");
        });

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.NotNull(eventStore);
        Assert.Null(snapshotStore);
        Assert.Null(projectionStore);
    }

    [Fact]
    public void AddSnapshotStoreOnly_RegistersOnlySnapshotStore()
    {
        var services = new ServiceCollection();

        services.AddSnapshotStoreOnly(options =>
        {
            options.UseInMemoryDatabase("SnapshotsDb");
        });

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.Null(eventStore);
        Assert.NotNull(snapshotStore);
        Assert.Null(projectionStore);
    }

    [Fact]
    public void AddProjectionStoreOnly_RegistersOnlyProjectionStore()
    {
        var services = new ServiceCollection();

        services.AddProjectionStoreOnly(options =>
        {
            options.UseInMemoryDatabase("ProjectionsDb");
        });

        var serviceProvider = services.BuildServiceProvider();

        var eventStore = serviceProvider.GetService<IEventStore>();
        var snapshotStore = serviceProvider.GetService<ISnapshotStore>();
        var projectionStore = serviceProvider.GetService<IProjectionStore>();

        Assert.Null(eventStore);
        Assert.Null(snapshotStore);
        Assert.NotNull(projectionStore);
    }

    [Fact]
    public void AddStoresSeparately_AllowsDifferentConfigurations()
    {
        var services = new ServiceCollection();

        // Register each store with different database
        services.AddEventStoreOnly(options =>
        {
            options.UseInMemoryDatabase("EventsDb");
        });

        services.AddSnapshotStoreOnly(options =>
        {
            options.UseInMemoryDatabase("SnapshotsDb");
        });

        services.AddProjectionStoreOnly(options =>
        {
            options.UseInMemoryDatabase("ProjectionsDb");
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
    public async Task AddStoresSeparately_WorksCorrectly()
    {
        var services = new ServiceCollection();

        services.AddEventStoreOnly(options =>
        {
            options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        });

        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();

        var pointer = new StreamPointer(new StreamIdentifier("Order", "1"), 0);
        var appendEvent = new AppendEvent(new OrderCreatedEvent(100), null);

        var result = await eventStore.AppendAsync(pointer, new[] { appendEvent });

        Assert.Single(result);
        Assert.Equal(1, result[0].StreamPointer.Version);
    }

    [Fact]
    public void AddEventStoreOnly_WithTransientLifetime_RegistersCorrectly()
    {
        var services = new ServiceCollection();

        services.AddEventStoreOnly(
            options => options.UseInMemoryDatabase("TestDb"),
            ServiceLifetime.Transient);

        var eventStoreDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventStore));

        Assert.NotNull(eventStoreDescriptor);
        Assert.Equal(ServiceLifetime.Transient, eventStoreDescriptor.Lifetime);
    }
}

