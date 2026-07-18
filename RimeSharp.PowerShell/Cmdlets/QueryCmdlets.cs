using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Get the last committed text from a RIME session.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeCommit")]
[OutputType(typeof(string))]
public sealed class GetRimeCommitCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

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

        using var commit = _rime.GetCommit(Session.Id);
        WriteObject(commit.Text);
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}

/// <summary>
/// Get the current input context from a RIME session (preedit, candidates, menu).
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeContext")]
[OutputType(typeof(RimeContext))]
public sealed class GetRimeContextCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

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

        var context = _rime.GetContext(Session.Id);
        WriteObject(context);
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}

/// <summary>
/// Get the current engine status from a RIME session (schema, mode flags).
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeStatus")]
[OutputType(typeof(RimeStatus))]
public sealed class GetRimeStatusCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

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

        var status = _rime.GetStatus(Session.Id);
        WriteObject(status);
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}
