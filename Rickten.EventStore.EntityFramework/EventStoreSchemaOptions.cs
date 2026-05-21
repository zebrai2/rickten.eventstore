namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Options for configuring the event store database schema.
/// </summary>
/// <param name="Schema">The database schema name (e.g., "users", "orders"). If null or empty, uses the provider's default schema.</param>
public sealed record EventStoreSchemaOptions(string Schema);
