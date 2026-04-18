Fixes #3

## Summary
Updates documentation and examples so event type filters use Rickten wire names (`{Aggregate}.{Name}.v{Version}`), not short event names.

## Problem
Docs currently show event filters using short names such as `"Created"`, `"Paid"`, `"SessionStarted"`, `"SessionCompleted"`, but the implementation filters against stored wire names like `"Order.Created.v1"`, `"Order.Paid.v1"`, `"SessionReview.SessionStarted.v1"`.

This causes consumers to get empty results when following the docs.

## Changes Made

### README.md
- Fixed event filter example to use wire names: `"Order.Created.v1"`, `"Order.Paid.v1"` instead of short names
- Added explanation of wire name format: `{Aggregate}.{Name}.v{Version}`
- Clarified that filters must match `[Event]` attribute values

### Rickten.Projector/README.md
- Updated Quick Start example to use `"SessionReview.SessionStarted.v1"`, `"SessionReview.SessionCompleted.v1"`, `"SessionReview.SessionCancelled.v1"`
- Enhanced `[Projection]` attribute documentation to explicitly state wire-name format requirement
- Fixed Event-Specific Projection examples
- Added clarification that EventTypes must match stored wire names

### docs/API.md
- Enhanced `LoadAllAsync` documentation to clarify `eventsFilter` parameter uses wire-name format
- Added examples showing wire name usage
- Updated `EventAttribute` section with comprehensive wire name format explanation

## Testing
- ✅ Build successful
- ✅ All 15 Rickten.Projector.Tests pass
- ✅ All 9 EventStoreFilteringTests pass
- ✅ Tests already use wire names correctly (no changes needed)

## Acceptance Criteria Met
- ✅ No docs examples use short event names in `eventsFilter` or `[Projection(EventTypes = ...)]`
- ✅ Docs explicitly state the wire-name filter format: `{Aggregate}.{Name}.v{Version}`
- ✅ Examples are internally consistent with their `[Event(...)]` attributes
- ✅ Existing tests pass
