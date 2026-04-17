# Rickten.Reactor - Design Notes

## TypeMetadataRegistry Integration

### Why Reactions Implement ITypeMetadata

Although reactions are not serialized, `ReactionAttribute` implements `ITypeMetadata` to gain:

1. **Duplicate Detection**: The registry throws on duplicate wire names, preventing naming conflicts
2. **Assembly Scanning**: Reactions are automatically discovered when assemblies are registered
3. **Consistent Pattern**: Events, Projections, and Reactions all follow the same registration pattern
4. **Tooling Support**: Future CLI tools can enumerate and inspect reactions

**Wire Name Format:** `Reaction.{Name}.{ClassName}`

Example:
```csharp
[Reaction("MembershipDefinitionChanged")]
public sealed class MembershipDefinitionChangedReaction : Reaction<...> { }
```
Produces wire name: `Reaction.MembershipDefinitionChanged.MembershipDefinitionChangedReaction`

### Registration

Reactions are registered when assemblies are scanned:

```csharp
var builder = new TypeMetadataRegistryBuilder();
builder.AddAssembly(typeof(MyReaction).Assembly);
var registry = builder.Build();

// Query metadata
var metadata = registry.GetMetadataByType(typeof(MyReaction));
var type = registry.GetTypeByWireName("Reaction.MyReaction.MyReaction");
```

### Why Not Just Store Reaction Names?

Using `ITypeMetadata` provides more than just name storage:
- Automatic duplicate prevention (no need to manually track names)
- Consistent discovery mechanism across all Rickten components
- Rich metadata (description, event type filters, etc.)
- Future extensibility (versioning, deprecation warnings, etc.)

## Projection Ownership Model

### The Problem
Initially, we considered having reactions use `ProjectionRunner.RebuildUntilAsync` to rebuild projections up to each trigger event. This had a critical flaw: if users ran that method outside of a reaction context, they could corrupt their projection state.

### The Solution: Reaction-Owned Projection State

Each reaction maintains **its own private projection state**, stored in `IReactionStore`, completely separate from any public `IProjectionStore` instances.

**Public projection use:**
```csharp
await ProjectionRunner.CatchUpAsync(
    eventStore, 
    publicProjectionStore,  // User's projection store
    projection, 
    "MyPublicProjection");
```

**Reaction projection use (internal):**
```csharp
// ReactionRunner manages this automatically
// Stores projection state in IReactionStore under the reaction key
var projectionState = await reactionStore.LoadReactionProjectionAsync<TView>(reactionName);
```

### Benefits

1. **No API Pollution**: `ProjectionRunner` has no dangerous or confusing bounded methods
2. **Separation of Concerns**: Public projections ≠ reaction-private projections
3. **Independent Lifecycles**: 
   - Users can reset/rebuild their public projections independently
   - Reactions manage their own projection state
   - Same `IProjection<TView>` class serves both purposes
4. **Performance**: 
   - Projection filters work normally (aggregate/event type filters)
   - Efficient catch-up from last projection checkpoint
   - Don't rebuild from scratch on every trigger
5. **Simplicity**: The runner handles projection state transparently

## Two-Checkpoint Model

Each reaction maintains two positions:

### 1. Reaction Checkpoint
- **Meaning**: Last trigger event where all commands succeeded
- **Advances**: Only after all commands for a trigger event complete successfully
- **Failure behavior**: If any command fails, checkpoint is NOT saved (at-least-once)

### 2. Projection Checkpoint
- **Meaning**: Last event applied to the reaction's private projection
- **Advances**: As events are applied to projection (may be ahead of reaction checkpoint)
- **Purpose**: Enables efficient catch-up without rebuilding projection from scratch

### Critical: Projection Ahead of Reaction

**Problem scenario:**
- Projection caught up to position 100
- Reaction failed at position 50
- On restart, we need to process triggers 51-100

**The bug (fixed):**
If we used `min(reactionPos, projectionPos)` and loaded from position 50, we'd process trigger event 51 with projection state at position 100, which includes FUTURE events!

**The fix:**
```csharp
if (projectionPosition > reactionPosition)
{
    // Rebuild projection from scratch up to reaction position
    projectionView = reaction.Projection.InitialView();
    await foreach (var event in LoadAllAsync(0, ...))
    {
        if (event.GlobalPosition > reactionPosition) break;
        projectionView = Apply(projectionView, event);
    }
    fromPosition = reactionPosition;
}
else
{
    // Projection behind or equal, use existing state
    fromPosition = projectionPosition;
}
```

This ensures the projection view represents **correct historical state** when processing old triggers.

### Catch-Up Flow

```
Stream: [E1] [E2] [E3:Trigger] [E4] [E5:Trigger] [E6] [E7:Trigger]

Checkpoints:
  Reaction: 0
  Projection: 0

Pass 1: Read from min(0,0)=0
  E1: Apply to projection (proj=1, reaction=0)
  E2: Apply to projection (proj=2, reaction=0)
  E3: Apply to projection (proj=3), then TRIGGER
      → SelectStreams(view, E3)
      → Execute commands
      → Save both checkpoints (reaction=3, proj=3)
  E4: Apply to projection (proj=4, reaction=3)
  E5: Apply to projection (proj=5), then TRIGGER
      → SelectStreams(view, E5)
      → Execute commands
      → Save both checkpoints (reaction=5, proj=5)
  ...

Pass 2: Read from min(5,5)=5
  (continues from checkpoint)
```

## Deterministic Projection Views

The projection view always represents deterministic state because:

1. **Incremental updates**: Projection is caught up event-by-event as the stream is read
2. **Sequential processing**: When trigger E_N fires, projection already reflects E_1...E_{N-1}
3. **Natural consistency**: By the time we call `SelectStreams(view, trigger)`, the view includes all events up to (but not past) the trigger

This is simpler than bounded catch-up and doesn't require special projection methods.

## Why Not Just Use LoadAllAsync with Trigger Filter?

We need to apply **all events** to the projection (respecting projection filters), but only **trigger events** should execute commands.

Example:
```
[UserRegistered] → Update projection (adds user to membership mapping)
[DefinitionChanged] → Update projection + TRIGGER (commands to affected memberships)
[OrderPlaced] → Update projection (if projection cares)
[DefinitionChanged] → Update projection + TRIGGER
```

The projection needs to see all relevant events to maintain accurate state for `SelectStreams`.

## Future Considerations

### Projection Checkpoint Optimization
Currently, we save projection checkpoint with every reaction checkpoint. Could optimize:
- Save projection checkpoint less frequently (every N events)
- Trade-off: More events to replay on restart vs. more saves

### Projection Snapshotting
For large projections, could extend `IReactionStore`:
```csharp
Task<ProjectionSnapshot<TView>?> LoadReactionProjectionSnapshotAsync<TView>(...);
Task SaveReactionProjectionSnapshotAsync<TView>(long position, TView state, ...);
```

### Multiple Reactions Sharing a Projection
If many reactions use the same projection, could optimize by:
- Detecting shared projections
- Maintaining one shared projection checkpoint
- But this adds complexity - defer until proven necessary

## Testing Strategy

Tests verify:
1. ✅ Multi-stream selection from single trigger
2. ✅ Checkpoint resumption (projection state preserved)
3. ✅ Event type filtering (only triggers execute commands)
4. ✅ Multiple independent reactions (separate states)
5. ✅ Projection view correctness (includes events before trigger)

The in-memory test store implements both reaction and projection checkpoint storage.
