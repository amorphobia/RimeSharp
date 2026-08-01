using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Enumerate candidates by their global index.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeCandidate")]
[OutputType(typeof(RimeCandidateInfo))]
public sealed class GetRimeCandidateCmdlet : RimeSessionCmdlet
{
    [Parameter]
    [ValidateRange(0, int.MaxValue)]
    public int Start { get; set; }

    [Parameter]
    [ValidateRange(0, int.MaxValue)]
    public int Count { get; set; } = int.MaxValue;

    protected override void ProcessRecord()
    {
        RimeCandidateInfo[] result;
        using (AcquireNativeGate())
        {
            var session = RimeProcessRuntime.RequireSession(this, Session);
            var rime = RimeProcessRuntime.ValidateSession(this, session);
            ThrowIfStopping();

            var candidates = rime.GetCandidates(session.NativeId, Start, Count);
            result = new RimeCandidateInfo[candidates.Length];
            for (var i = 0; i < candidates.Length; ++i)
            {
                result[i] = new RimeCandidateInfo(Start + i, candidates[i]);
            }

            ThrowIfStopping();
        }

        foreach (var candidate in result)
        {
            WriteObject(candidate, enumerateCollection: false);
        }
    }
}

/// <summary>
/// Select a candidate by its global or current-page index.
/// </summary>
[Cmdlet(VerbsCommon.Select, "RimeCandidate")]
[OutputType(typeof(void))]
public sealed class SelectRimeCandidateCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true)]
    [ValidateRange(0, int.MaxValue)]
    public int Index { get; set; }

    [Parameter]
    public SwitchParameter OnCurrentPage { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        var succeeded = rime.SelectCandidate(session.NativeId, Index, OnCurrentPage);
        ThrowIfStopping();
        if (!succeeded)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new ArgumentException(
                    $"Candidate index {Index} could not be selected.",
                    nameof(Index)),
                "RimeCandidateSelectionFailed",
                ErrorCategory.InvalidArgument,
                Index);
        }
    }
}

/// <summary>
/// Remove a candidate by its global or current-page index.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "RimeCandidate", SupportsShouldProcess = true)]
[OutputType(typeof(void))]
public sealed class RemoveRimeCandidateCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true)]
    [ValidateRange(0, int.MaxValue)]
    public int Index { get; set; }

    [Parameter]
    public SwitchParameter OnCurrentPage { get; set; }

    protected override void ProcessRecord()
    {
        if (!ShouldProcess($"candidate index {Index}", "Remove RIME candidate")) return;

        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        var succeeded = rime.DeleteCandidate(session.NativeId, Index, OnCurrentPage);
        ThrowIfStopping();
        if (!succeeded)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new ArgumentException(
                    $"Candidate index {Index} could not be removed.",
                    nameof(Index)),
                "RimeCandidateRemovalFailed",
                ErrorCategory.InvalidArgument,
                Index);
        }
    }
}

/// <summary>
/// Highlight a candidate by its global or current-page index.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "RimeHighlight")]
[OutputType(typeof(void))]
public sealed class InvokeRimeHighlightCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true)]
    [ValidateRange(0, int.MaxValue)]
    public int Index { get; set; }

    [Parameter]
    public SwitchParameter OnCurrentPage { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        var succeeded = rime.HighlightCandidate(session.NativeId, Index, OnCurrentPage);
        ThrowIfStopping();
        if (!succeeded)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new ArgumentException(
                    $"Candidate index {Index} could not be highlighted.",
                    nameof(Index)),
                "RimeCandidateHighlightFailed",
                ErrorCategory.InvalidArgument,
                Index);
        }
    }
}
