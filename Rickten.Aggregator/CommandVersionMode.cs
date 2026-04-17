namespace Rickten.Aggregator;

/// <summary>
/// Specifies whether a command executes against the latest aggregate stream version
/// or requires a caller-provided expected version for CQRS stale-read protection.
/// </summary>
public enum CommandVersionMode
{
    /// <summary>
    /// Execute against the latest aggregate stream version at command execution time.
    /// This is the default behavior and suitable for most commands, including Reactor side effects.
    /// </summary>
    LatestVersion,

    /// <summary>
    /// Execute only if the stream is still at the caller's expected version.
    /// Use this for CQRS command handling where the user's decision was based on a specific
    /// read model version, and the command should fail if the stream has changed since then.
    /// </summary>
    ExpectedVersion
}
