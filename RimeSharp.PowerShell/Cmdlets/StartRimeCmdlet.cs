using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Initialize the RIME engine and return a <see cref="RimeSession"/> for pipeline binding.
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "Rime")]
[OutputType(typeof(RimeSession))]
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
    public string? DistributionName { get; set; }

    [Parameter]
    public string? DistributionCodeName { get; set; }

    [Parameter]
    public string? DistributionVersion { get; set; }

    [Parameter]
    public string? Modules { get; set; }

    [Parameter]
    public int MinLogLevel { get; set; }

    [Parameter]
    public string? LogDir { get; set; }

    [Parameter]
    public string? PrebuiltDataDir { get; set; }

    [Parameter]
    public string? StagingDir { get; set; }

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

        if (DistributionName is not null) traits.DistributionName = DistributionName;
        if (DistributionCodeName is not null) traits.DistributionCodeName = DistributionCodeName;
        if (DistributionVersion is not null) traits.DistributionVersion = DistributionVersion;
        if (Modules is not null) traits.Modules = Modules;
        if (LogDir is not null) traits.LogDir = LogDir;
        if (PrebuiltDataDir is not null) traits.PrebuiltDataDir = PrebuiltDataDir;
        if (StagingDir is not null) traits.StagingDir = StagingDir;
        traits.MinLogLevel = MinLogLevel;

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

        var session = new RimeSession(sessionId);

        if (PassThru)
        {
            SessionState.PSVariable.Set("global:RimeDefaultSession", session);
        }

        WriteObject(session);
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
