using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Get the value of a boolean option for a RIME session.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeOption")]
[OutputType(typeof(bool))]
public sealed class GetRimeOptionCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true
    )]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = "";

    [Parameter(
        Position = 1,
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

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        WriteObject(_rime.GetOption(Session.Id, Name));
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}

/// <summary>
/// Set the value of a boolean option for a RIME session.
/// </summary>
[Cmdlet(VerbsCommon.Set, "RimeOption")]
[OutputType(typeof(void))]
public sealed class SetRimeOptionCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true
    )]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = "";

    [Parameter(
        Position = 1,
        Mandatory = true
    )]
    public bool Value { get; set; }

    [Parameter(
        Position = 2,
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

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        _rime.SetOption(Session.Id, Name, Value);
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}
