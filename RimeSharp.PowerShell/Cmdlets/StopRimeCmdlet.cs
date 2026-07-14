using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Destroy a RIME session and finalize the engine when no sessions remain.
/// </summary>
[Cmdlet(VerbsLifecycle.Stop, "Rime")]
[OutputType(typeof(void))]
public sealed class StopRimeCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true,
        ValueFromPipeline = true
    )]
    public RimeSession Session { get; set; } = null!;

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();
    }

    protected override void ProcessRecord()
    {
        if (_rime is null) return;

        if (!_rime.DestroySession(Session.Id))
        {
            var ex = new ArgumentException(
                $"Session {Session.Id} is invalid or already destroyed.", nameof(Session));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionInvalid",
                ErrorCategory.InvalidArgument, Session));
            return;
        }
    }

    protected override void EndProcessing()
    {
        _rime?.Finalize1();
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
