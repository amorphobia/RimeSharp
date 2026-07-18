using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Select a candidate by its global or current-page index.
/// </summary>
[Cmdlet(VerbsCommon.Select, "RimeCandidate")]
[OutputType(typeof(void))]
public sealed class SelectRimeCandidateCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true
    )]
    [ValidateRange(0, int.MaxValue)]
    public int Index { get; set; }

    [Parameter(
        Position = 1,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

    [Parameter]
    public SwitchParameter OnCurrentPage { get; set; }

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

        if (!_rime.SelectCandidate(Session.Id, Index, OnCurrentPage))
        {
            var ex = new ArgumentException(
                $"Candidate index {Index} could not be selected.", nameof(Index));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeCandidateSelectionFailed",
                ErrorCategory.InvalidArgument, Index));
        }
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}
