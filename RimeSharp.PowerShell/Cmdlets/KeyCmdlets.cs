using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Simulate a native key sequence and return its final atomic managed state.
/// </summary>
[Cmdlet(VerbsCommunications.Send, "RimeKey")]
[OutputType(typeof(RimeKeySequenceResult))]
public sealed class SendRimeKeyCmdlet : RimeCmdlet
{
    [Parameter(Mandatory = true)]
    [ValidateNotNull]
    public string Sequence { get; set; } = string.Empty;

    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        var bridge = RimeProcessRuntime.Bridge
            ?? throw new InvalidOperationException(
                "The active lifecycle has no notification bridge.");
        ThrowIfStopping();

        var capture = bridge.BeginCapture(session);
        RimeNotification[] notifications;
        bool succeeded;
        try
        {
            succeeded = rime.SimulateKeySequence(session.NativeId, Sequence);
        }
        finally
        {
            notifications = bridge.EndCapture(capture);
        }

        ThrowIfStopping();
        if (!succeeded)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new ArgumentException(
                    $"Key sequence '{Sequence}' could not be simulated.",
                    nameof(Sequence)),
                "RimeKeySequenceFailed",
                ErrorCategory.InvalidArgument,
                Sequence);
            return;
        }

        var snapshot = ReadSnapshot(rime, session);
        ThrowIfStopping();
        WriteObject(
            new RimeKeySequenceResult(
                snapshot.Commit,
                snapshot.Status,
                snapshot.Context,
                snapshot.Input,
                notifications),
            enumerateCollection: false);
    }

    private RimeOperationSnapshot ReadSnapshot(Rime rime, RimeSession session)
    {
        try
        {
            return RimeOperationSnapshot.Read(rime, session);
        }
        catch (Exception ex)
        {
            RimeProcessRuntime.ThrowError(
                this,
                ex,
                "RimeSnapshotFailed",
                ErrorCategory.ReadError,
                session);
            throw;
        }
    }
}

/// <summary>
/// Process one raw key event and return its atomic managed result.
/// </summary>
[Cmdlet(VerbsCommunications.Send, "RimeKeyEvent")]
[OutputType(typeof(RimeKeyEventResult))]
public sealed class SendRimeKeyEventCmdlet : RimeCmdlet
{
    [Parameter(Mandatory = true)]
    public int KeyCode { get; set; }

    [Parameter]
    public int Mask { get; set; }

    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        var bridge = RimeProcessRuntime.Bridge
            ?? throw new InvalidOperationException(
                "The active lifecycle has no notification bridge.");
        ThrowIfStopping();

        var capture = bridge.BeginCapture(session);
        RimeNotification[] notifications;
        bool handled;
        try
        {
            handled = rime.ProcessKey(session.NativeId, KeyCode, Mask);
        }
        finally
        {
            notifications = bridge.EndCapture(capture);
        }

        ThrowIfStopping();
        RimeOperationSnapshot snapshot;
        try
        {
            snapshot = RimeOperationSnapshot.Read(rime, session);
        }
        catch (Exception ex)
        {
            RimeProcessRuntime.ThrowError(
                this,
                ex,
                "RimeSnapshotFailed",
                ErrorCategory.ReadError,
                session);
            return;
        }

        ThrowIfStopping();
        WriteObject(
            new RimeKeyEventResult(
                handled,
                snapshot.Commit,
                snapshot.Status,
                snapshot.Context,
                snapshot.Input,
                notifications),
            enumerateCollection: false);
    }
}

internal sealed class RimeOperationSnapshot
{
    internal string? Commit { get; }
    internal RimeStatusSnapshot Status { get; }
    internal RimeContextSnapshot Context { get; }
    internal string Input { get; }

    private RimeOperationSnapshot(
        string? commit,
        RimeStatusSnapshot status,
        RimeContextSnapshot context,
        string input)
    {
        Commit = commit;
        Status = status;
        Context = context;
        Input = input;
    }

    internal static RimeOperationSnapshot Read(Rime rime, RimeSession session)
    {
        using var commit = rime.GetCommit(session.NativeId);
        using var status = rime.GetStatus(session.NativeId);
        using var context = rime.GetContext(session.NativeId);
        var input = rime.GetInput(session.NativeId) ?? string.Empty;

        return new RimeOperationSnapshot(
            commit.Text,
            new RimeStatusSnapshot(status),
            new RimeContextSnapshot(context),
            input);
    }
}
