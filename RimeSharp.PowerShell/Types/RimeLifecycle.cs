using System.Collections.ObjectModel;

namespace RimeSharp.PowerShell;

/// <summary>
/// Describes the state of the process-wide RIME lifecycle owned by this module.
/// </summary>
public enum RimeLifecycleState
{
    Inactive,
    Starting,
    Active,
    Stopping,
    Faulted,
}

/// <summary>
/// Identifies the lifecycle operation represented by a failure diagnostic.
/// </summary>
public enum RimeLifecycleOperation
{
    Start,
    Stop,
    NewSessionRollback,
}

/// <summary>
/// Describes a lifecycle failure and any later cleanup failures.
/// </summary>
public sealed class RimeLifecycleFailureInfo
{
    public RimeLifecycleOperation Operation { get; }
    public RimeLifecycleState FinalState { get; }
    public Exception PrimaryFailure { get; }
    public IReadOnlyList<Exception> CleanupFailures { get; }

    internal RimeLifecycleFailureInfo(
        RimeLifecycleOperation operation,
        RimeLifecycleState finalState,
        Exception primaryFailure,
        IEnumerable<Exception> cleanupFailures)
    {
        Operation = operation;
        FinalState = finalState;
        PrimaryFailure = primaryFailure;
        CleanupFailures = new ReadOnlyCollection<Exception>(cleanupFailures.ToArray());
    }
}
