using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Specifies the direction in which to move through candidate pages.
/// </summary>
public enum PageDirection
{
    Next,
    Previous,
}

/// <summary>
/// Move to the next or previous candidate page.
/// </summary>
[Cmdlet(VerbsCommon.Set, "RimePage")]
[OutputType(typeof(void))]
public sealed class SetRimePageCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true
    )]
    public PageDirection Direction { get; set; }

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
        var backward = Direction == PageDirection.Previous;
        if (!_rime.ChangePage(Session.Id, backward))
        {
            var ex = new InvalidOperationException(
                $"Could not move to the {Direction.ToString().ToLowerInvariant()} candidate page.");
            ThrowTerminatingError(new ErrorRecord(ex, "RimePageChangeFailed",
                ErrorCategory.InvalidOperation, Session));
        }
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}
