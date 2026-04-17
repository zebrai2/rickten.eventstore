namespace Rickten.Aggregator;

/// <summary>
/// Interface for commands that carry an expected stream version for optimistic concurrency control.
/// Commands implementing this interface should be decorated with
/// [Command(..., VersionMode = CommandVersionMode.ExpectedVersion)].
/// </summary>
public interface IExpectedVersionCommand
{
    /// <summary>
    /// Gets the expected stream version that informed the user's decision to execute this command.
    /// The command will only execute if the stream is still at this version.
    /// </summary>
    long ExpectedVersion { get; }
}
