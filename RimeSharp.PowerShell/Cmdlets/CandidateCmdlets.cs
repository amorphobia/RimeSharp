using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Enumerate candidates by their global index.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeCandidate")]
[OutputType(typeof(RimeCandidateInfo))]
public sealed class GetRimeCandidateCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(Position = 0)]
    [ValidateRange(0, int.MaxValue)]
    public int Start { get; set; }

    [Parameter(Position = 1)]
    [ValidateRange(0, int.MaxValue)]
    public int Count { get; set; } = int.MaxValue;

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
        var candidates = _rime.GetCandidates(Session.Id, Start, Count);
        for (var i = 0; i < candidates.Length; ++i)
        {
            WriteObject(new RimeCandidateInfo(Start + i, candidates[i]));
        }
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}

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

        SessionValidation.EnsureSessionValid(this, _rime, Session);
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

/// <summary>
/// Remove a candidate by its global or current-page index.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "RimeCandidate", SupportsShouldProcess = true)]
[OutputType(typeof(void))]
public sealed class RemoveRimeCandidateCmdlet : PSCmdlet, IDisposable
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

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        if (!ShouldProcess($"candidate index {Index}", "Remove RIME candidate"))
        {
            return;
        }

        if (!_rime.DeleteCandidate(Session.Id, Index, OnCurrentPage))
        {
            var ex = new ArgumentException(
                $"Candidate index {Index} could not be removed.", nameof(Index));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeCandidateRemovalFailed",
                ErrorCategory.InvalidArgument, Index));
        }
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}

/// <summary>
/// Highlight a candidate by its global or current-page index.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "RimeHighlight")]
[OutputType(typeof(void))]
public sealed class InvokeRimeHighlightCmdlet : PSCmdlet, IDisposable
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

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        if (!_rime.HighlightCandidate(Session.Id, Index, OnCurrentPage))
        {
            var ex = new ArgumentException(
                $"Candidate index {Index} could not be highlighted.", nameof(Index));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeCandidateHighlightFailed",
                ErrorCategory.InvalidArgument, Index));
        }
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}
