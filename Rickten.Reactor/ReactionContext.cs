namespace Rickten.Reactor;

/// <summary>
/// Context created when a trigger fires. Contains trigger identity and occurrence data.
/// Acts as a dictionary-like bag for trigger-specific data (events, schedules, endpoints, etc.).
/// </summary>
public sealed class ReactionContext
{
    /// <summary>
    /// Empty context for testing or initialization.
    /// </summary>
    public static ReactionContext Empty { get; } = new(new Dictionary<string, object?>());

    private readonly IReadOnlyDictionary<string, object?> _values;

    /// <summary>
    /// Creates a new reaction context with the specified values.
    /// </summary>
    /// <param name="values">Context data dictionary.</param>
    public ReactionContext(IReadOnlyDictionary<string, object?> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>
    /// Gets the value for the specified key.
    /// </summary>
    /// <param name="key">The context key.</param>
    /// <returns>The value, or null if not found.</returns>
    public object? this[string key] => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Tries to get a value from the context.
    /// </summary>
    /// <param name="key">The context key.</param>
    /// <param name="value">The value if found.</param>
    /// <returns>True if the key exists, false otherwise.</returns>
    public bool TryGetValue(string key, out object? value)
        => _values.TryGetValue(key, out value);

    /// <summary>
    /// Creates a new context with an additional or updated value.
    /// </summary>
    /// <param name="key">The context key.</param>
    /// <param name="value">The value to add or update.</param>
    /// <returns>A new context with the updated value.</returns>
    public ReactionContext With(string key, object? value)
    {
        var next = new Dictionary<string, object?>(_values)
        {
            [key] = value
        };

        return new ReactionContext(next);
    }

    /// <summary>
    /// Gets all keys in this context.
    /// </summary>
    public IEnumerable<string> Keys => _values.Keys;

    /// <summary>
    /// Gets the number of values in this context.
    /// </summary>
    public int Count => _values.Count;
}
