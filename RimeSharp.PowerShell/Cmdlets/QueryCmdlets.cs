using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Consume and return the current unread commit, if one exists.
/// </summary>
[Cmdlet(VerbsCommunications.Receive, "RimeCommit")]
[OutputType(typeof(string))]
public sealed class ReceiveRimeCommitCmdlet : RimeCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        using var commit = rime.GetCommit(session.NativeId);
        var text = commit.Text;
        ThrowIfStopping();
        if (text is not null)
        {
            WriteObject(text);
        }
    }
}

/// <summary>
/// Return the current raw input as a non-null string.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeInput")]
[OutputType(typeof(string))]
public sealed class GetRimeInputCmdlet : RimeCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        var input = rime.GetInput(session.NativeId) ?? string.Empty;
        ThrowIfStopping();
        WriteObject(input);
    }
}

/// <summary>
/// Return a managed snapshot of the current input context.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeContext")]
[OutputType(typeof(RimeContextSnapshot))]
public sealed class GetRimeContextCmdlet : RimeCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        try
        {
            using var context = rime.GetContext(session.NativeId);
            var snapshot = new RimeContextSnapshot(context);
            ThrowIfStopping();
            WriteObject(snapshot, enumerateCollection: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RimeProcessRuntime.ThrowError(
                this,
                ex,
                "RimeSnapshotFailed",
                ErrorCategory.ReadError,
                session);
        }
    }
}

/// <summary>
/// Return a managed snapshot of the current engine status.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeStatus")]
[OutputType(typeof(RimeStatusSnapshot))]
public sealed class GetRimeStatusCmdlet : RimeCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        try
        {
            using var status = rime.GetStatus(session.NativeId);
            var snapshot = new RimeStatusSnapshot(status);
            ThrowIfStopping();
            WriteObject(snapshot, enumerateCollection: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RimeProcessRuntime.ThrowError(
                this,
                ex,
                "RimeSnapshotFailed",
                ErrorCategory.ReadError,
                session);
        }
    }
}
