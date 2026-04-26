# Rickten.Reactor

Trigger-based reaction system for event-driven architectures.

## Core Model

### TriggerType

A trigger type is the **mechanism** - it knows how to create and run trigger instances.

**Examples:**
- EventStore - React to events in the event store
- Recurring - React on a schedule (cron, interval)
- Delayed - React after a delay
- Endpoint - React to HTTP/gRPC calls

### TriggerInstance

A trigger instance is a **uniquely named, configured source** that fires reaction contexts.

**Example:**
name: OnOrderSubmitted
type: EventStore
config:
  eventTypes: [Order.Submitted.v1]

**Multiple reactions can share the same trigger instance:**
StartFulfillment   -> OnOrderSubmitted
SendConfirmation   -> OnOrderSubmitted
ReserveInventory   -> OnOrderSubmitted

Instance names must be **unique** within the application/registry.

### ReactionContext

A trigger firing creates a ReactionContext - a dictionary-like bag of data about the occurrence.

**Trigger identity:**
trigger.name = OnOrderSubmitted
trigger.type = EventStore

**Occurrence data (added by trigger):**
For EventStore triggers:
  event.type = Order.Submitted.v1
  event.globalPosition = 1234
  event.stream = Order/abc

For Recurring triggers:
  schedule.name = DailyReports
  schedule.time = 2024-01-15T08:00:00Z

For Endpoint triggers:
  request.method = POST
  request.path = /webhooks/stripe

## Design Rules

1. A trigger instance is a named configured source that starts a ReactionContext
2. Multiple reactions can share the same trigger instance
3. Context is a flexible bag - no rigid trigger-kind coupling
4. Trigger types are extensible - implement IReactionTriggerType
