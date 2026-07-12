using System.Management.Automation;
using RimeSharp;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Initialize the RIME engine and return a session ID.
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "Rime")]
[OutputType(typeof(UIntPtr))]
public sealed class StartRimeCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(Position = 0)]
    public string AppName { get; set; } = "RimeSharp.PowerShell";

    [Parameter(Position = 1)]
    public string SharedDataDir { get; set; } = "shared";

    [Parameter(Position = 2)]
    public string UserDataDir { get; set; } = "user";

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();

        var traits = new RimeTraits
        {
            AppName = AppName,
            SharedDataDir = SharedDataDir,
            UserDataDir = UserDataDir,
        };

        _rime.Setup(ref traits);
        _rime.Initialize(ref traits);

        if (_rime.StartMaintenance(fullCheck: true))
        {
            _rime.JoinMaintenanceThread();
        }

        var sessionId = _rime.CreateSession();
        if (sessionId == 0)
        {
            var ex = new InvalidOperationException("Failed to create RIME session.");
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionFailed",
                ErrorCategory.ResourceUnavailable, null));
            return;
        }

        WriteObject(sessionId);
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
