using System;
using System.Collections.Generic;
using Rickten.EventStore;
using Xunit;

namespace Rickten.EventStore.Tests;

/// <summary>
/// Tests for trace identity metadata (EventId, CorrelationId, BatchId, CausationId).
/// </summary>
public class TraceIdentityMetadataTests
{
    [Fact]
    public void EventMetadataKeys_Constants_Are_Defined()
    {
        Assert.Equal("EventId", EventMetadataKeys.EventId);
        Assert.Equal("CorrelationId", EventMetadataKeys.CorrelationId);
        Assert.Equal("CausationId", EventMetadataKeys.CausationId);
        Assert.Equal("BatchId", EventMetadataKeys.BatchId);
    }

    [Fact]
    public void GetEventId_Returns_Null_When_Not_Present()
    {
        var metadata = new List<EventMetadata>();

        var result = metadata.GetEventId();

        Assert.Null(result);
    }

    [Fact]
    public void GetEventId_Returns_Guid_When_Present()
    {
        var expectedId = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.System, EventMetadataKeys.EventId, expectedId)
        };

        var result = metadata.GetEventId();

        Assert.Equal(expectedId, result);
    }

    [Fact]
    public void GetCorrelationId_Returns_Null_When_Not_Present()
    {
        var metadata = new List<EventMetadata>();

        var result = metadata.GetCorrelationId();

        Assert.Null(result);
    }

    [Fact]
    public void GetCorrelationId_Returns_Guid_When_Present()
    {
        var expectedId = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.Client, EventMetadataKeys.CorrelationId, expectedId)
        };

        var result = metadata.GetCorrelationId();

        Assert.Equal(expectedId, result);
    }

    [Fact]
    public void GetCausationId_Returns_Null_When_Not_Present()
    {
        var metadata = new List<EventMetadata>();

        var result = metadata.GetCausationId();

        Assert.Null(result);
    }

    [Fact]
    public void GetCausationId_Returns_Guid_When_Present()
    {
        var expectedId = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.Client, EventMetadataKeys.CausationId, expectedId)
        };

        var result = metadata.GetCausationId();

        Assert.Equal(expectedId, result);
    }

    [Fact]
    public void GetBatchId_Returns_Null_When_Not_Present()
    {
        var metadata = new List<EventMetadata>();

        var result = metadata.GetBatchId();

        Assert.Null(result);
    }

    [Fact]
    public void GetBatchId_Returns_Guid_When_Present()
    {
        var expectedId = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.System, EventMetadataKeys.BatchId, expectedId)
        };

        var result = metadata.GetBatchId();

        Assert.Equal(expectedId, result);
    }

    [Fact]
    public void EventMetadataSource_Constants_Are_Defined()
    {
        Assert.Equal("Client", EventMetadataSource.Client);
        Assert.Equal("System", EventMetadataSource.System);
        Assert.Equal("Application", EventMetadataSource.Application);
    }

    [Fact]
    public void GetSystemEventId_Returns_Null_When_Not_Present()
    {
        var metadata = new List<EventMetadata>();

        var result = metadata.GetSystemEventId();

        Assert.Null(result);
    }

    [Fact]
    public void GetSystemEventId_Returns_System_EventId_When_Present()
    {
        var systemEventId = Guid.NewGuid();
        var clientEventId = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.Client, EventMetadataKeys.EventId, clientEventId),
            new EventMetadata(EventMetadataSource.System, EventMetadataKeys.EventId, systemEventId)
        };

        var result = metadata.GetSystemEventId();

        Assert.Equal(systemEventId, result);
    }

    [Fact]
    public void GetSystemEventId_Ignores_Client_EventId()
    {
        var clientEventId = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.Client, EventMetadataKeys.EventId, clientEventId)
        };

        var result = metadata.GetSystemEventId();

        Assert.Null(result);
    }

    [Fact]
    public void GetGuid_WithSource_Returns_Value_Matching_Source()
    {
        var clientId = Guid.NewGuid();
        var systemId = Guid.NewGuid();
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.Client, "TestKey", clientId),
            new EventMetadata(EventMetadataSource.System, "TestKey", systemId)
        };

        var clientResult = metadata.GetGuid("TestKey", EventMetadataSource.Client);
        var systemResult = metadata.GetGuid("TestKey", EventMetadataSource.System);

        Assert.Equal(clientId, clientResult);
        Assert.Equal(systemId, systemResult);
    }

    [Fact]
    public void GetReactionWireName_Returns_Null_When_Not_Present()
    {
        var metadata = new List<EventMetadata>();

        var result = metadata.GetReactionWireName();

        Assert.Null(result);
    }

    [Fact]
    public void GetReactionWireName_Returns_String_When_Present()
    {
        var expectedWireName = "Reaction.MembershipChanged.MembershipReaction";
        var metadata = new List<EventMetadata>
        {
            new EventMetadata(EventMetadataSource.Client, EventMetadataKeys.ReactionWireName, expectedWireName)
        };

        var result = metadata.GetReactionWireName();

        Assert.Equal(expectedWireName, result);
    }
}

