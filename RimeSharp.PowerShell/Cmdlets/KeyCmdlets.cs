using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Simulate a key sequence (e.g. "nihao" or "Down") and return a
/// <see cref="RimeResponse"/> containing commit, status, and context.
/// Internally calls <c>SimulateKeySequence</c>.
/// </summary>
[Cmdlet(VerbsCommunications.Send, "RimeKey")]
[OutputType(typeof(RimeResponse))]
public sealed class SendRimeKeyCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 1,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

    [Parameter(
        Position = 0,
        Mandatory = true
    )]
    public string Sequence { get; set; } = "";

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();
        Session ??= SessionState.PSVariable.GetValue("global:RimeDefaultSession") as RimeSession;
    }

    protected override void ProcessRecord()
    {
        if (_rime is null) return;
        if (Session is null)
        {
            var ex = new InvalidOperationException(
                "No RIME session specified. Pipe a session from Start-Rime or use -Session.");
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionMissing",
                ErrorCategory.InvalidOperation, null));
            return;
        }

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        if (!_rime.SimulateKeySequence(Session.Id, Sequence))
        {
            var ex = new ArgumentException(
                $"Key sequence '{Sequence}' could not be simulated.", nameof(Sequence));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeKeySequenceFailed",
                ErrorCategory.InvalidArgument, Sequence));
            return;
        }

        using var commit  = _rime.GetCommit(Session.Id);
        using var status  = _rime.GetStatus(Session.Id);
        using var context = _rime.GetContext(Session.Id);

        var response = new RimeResponse(
            commit.Text,
            new RimeStatusSnapshot(status),
            new RimeContextSnapshot(context));
        WriteObject(response);
    }

    protected override void StopProcessing()
    {
        Dispose();
    }

    public void Dispose()
    {
        _rime = null;
    }
}

/// <summary>
/// Send a single raw key event (key code + modifier mask) to a RIME session.
/// Uses <c>ProcessKey</c> for low-level key handling.
/// </summary>
[Cmdlet(VerbsCommunications.Send, "RimeKeyEvent")]
[OutputType(typeof(bool))]
public sealed class SendRimeKeyEventCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 2,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

    [Parameter(
        Position = 0,
        Mandatory = true
    )]
    public int KeyCode { get; set; }

    [Parameter(Position = 1)]
    public int Mask { get; set; }

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();
        Session ??= SessionState.PSVariable.GetValue("global:RimeDefaultSession") as RimeSession;
    }

    protected override void ProcessRecord()
    {
        if (_rime is null) return;
        if (Session is null)
        {
            var ex = new InvalidOperationException(
                "No RIME session specified. Pipe a session from Start-Rime or use -Session.");
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionMissing",
                ErrorCategory.InvalidOperation, null));
            return;
        }

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        var handled = _rime.ProcessKey(Session.Id, KeyCode, Mask);
        WriteObject(handled);
    }

    protected override void StopProcessing()
    {
        Dispose();
    }

    public void Dispose()
    {
        _rime = null;
    }
}
