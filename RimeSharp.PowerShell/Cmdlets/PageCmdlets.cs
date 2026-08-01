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
public sealed class SetRimePageCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true)]
    public PageDirection Direction { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        var succeeded = rime.ChangePage(
            session.NativeId,
            Direction == PageDirection.Previous);
        ThrowIfStopping();
        if (!succeeded)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new InvalidOperationException(
                    $"Could not move to the {Direction.ToString().ToLowerInvariant()} candidate page."),
                "RimePageChangeFailed",
                ErrorCategory.InvalidOperation,
                session);
        }
    }
}
