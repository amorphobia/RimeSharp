using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Get the display label for a RIME option state.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeStateLabel")]
[OutputType(typeof(string))]
public sealed class GetRimeStateLabelCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true,
        ValueFromPipelineByPropertyName = true
    )]
    [Alias("OptionName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = "";

    [Parameter(
        Position = 1,
        Mandatory = true,
        ValueFromPipelineByPropertyName = true
    )]
    [Alias("OptionState")]
    public bool State { get; set; }

    [Parameter(
        Position = 2,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    public RimeSession? Session { get; set; }

    [Parameter]
    public SwitchParameter Abbreviated { get; set; }

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
        WriteObject(_rime.GetStateLabel(Session.Id, Name, State, Abbreviated));
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}
