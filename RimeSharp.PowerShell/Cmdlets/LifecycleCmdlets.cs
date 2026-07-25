using System.Management.Automation;
using System.Threading;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Initialize the RIME engine and return a <see cref="RimeSession"/> for pipeline binding.
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "Rime")]
[OutputType(typeof(RimeSession))]
public sealed class StartRimeCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;
    private bool _isSetup;
    private int _stopRequested;

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
    public SwitchParameter SetDefaultSession { get; set; }

    protected override void BeginProcessing()
    {
        if (!RimeEngineLifecycle.TryBeginStart())
        {
            var ex = new InvalidOperationException(
                "RIME is already started in this process. Stop the active session before starting another.");
            ThrowTerminatingError(new ErrorRecord(
                ex,
                "RimeAlreadyStarted",
                ErrorCategory.InvalidOperation,
                null));
            return;
        }

        try
        {
            StartEngine();
        }
        catch
        {
            CleanupFailedStart();
            RimeEngineLifecycle.CompleteStop();
            throw;
        }
    }

    private void StartEngine()
    {
        _rime = Rime.Instance();
        ThrowIfStopRequested();

        SharedDataDir = ResolvePath(SharedDataDir);
        UserDataDir = ResolvePath(UserDataDir);
        LogDir = ResolveOptionalPath(LogDir);
        PrebuiltDataDir = ResolveOptionalPath(PrebuiltDataDir);
        StagingDir = ResolveOptionalPath(StagingDir);

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
        _isSetup = true;
        ThrowIfStopRequested();
        RimeNotificationBridge.OnEngineSetup(_rime);
        _rime.Initialize(ref traits);
        ThrowIfStopRequested();

        if (_rime.StartMaintenance(fullCheck: true))
        {
            _rime.JoinMaintenanceThread();
        }
        ThrowIfStopRequested();

        var sessionId = _rime.CreateSession();
        if (sessionId == 0)
        {
            var ex = new InvalidOperationException("Failed to create RIME session.");
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionFailed",
                ErrorCategory.ResourceUnavailable, null));
            return;
        }
        ThrowIfStopRequested();

        var session = new RimeSession(sessionId);
        WriteObject(session);

        if (SetDefaultSession)
        {
            SessionState.PSVariable.Set("global:RimeDefaultSession", session);
        }
    }

    private void CleanupFailedStart()
    {
        if (!_isSetup || _rime is null) return;

        try
        {
            RimeNotificationBridge.OnEngineFinalizing(_rime);
            _rime.Finalize1();
        }
        catch
        {
            // Preserve the original startup failure.
        }
    }

    protected override void StopProcessing()
    {
        Volatile.Write(ref _stopRequested, 1);
    }

    public void Dispose()
    {
        _rime = null;
    }

    private void ThrowIfStopRequested()
    {
        if (Volatile.Read(ref _stopRequested) != 0)
        {
            throw new OperationCanceledException("Start-Rime was stopped.");
        }
    }

    private string ResolvePath(string path)
        => SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

    private string? ResolveOptionalPath(string? path)
        => string.IsNullOrEmpty(path) ? path : ResolvePath(path);
}

/// <summary>
/// Destroy the active RIME session and finalize the single engine lifecycle.
/// </summary>
[Cmdlet(VerbsLifecycle.Stop, "Rime")]
[OutputType(typeof(void))]
public sealed class StopRimeCmdlet : PSCmdlet, IDisposable
{
    private readonly object _finalizationSync = new();
    private Rime? _rime;
    private bool _isFinalized;
    private bool _sessionDestroyed;

    [Parameter(
        Position = 0,
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
        lock (_finalizationSync)
        {
            if (_isFinalized) return;
            StopSession();
        }
    }

    private void StopSession()
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

        if (!_rime.DestroySession(Session.Id))
        {
            var ex = new ArgumentException(
                $"Session {Session.Id} is invalid or already destroyed.", nameof(Session));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionInvalid",
                ErrorCategory.InvalidArgument, Session));
            return;
        }
        _sessionDestroyed = true;

        var defaultSession = SessionState.PSVariable.GetValue(
            "global:RimeDefaultSession") as RimeSession;
        if (defaultSession?.Id == Session.Id)
        {
            SessionState.PSVariable.Remove("global:RimeDefaultSession");
        }
    }

    protected override void EndProcessing()
    {
        FinalizeEngine();
    }

    protected override void StopProcessing()
    {
        FinalizeEngine();
    }

    public void Dispose()
    {
        FinalizeEngine();
    }

    private void FinalizeEngine()
    {
        lock (_finalizationSync)
        {
            if (_isFinalized || !_sessionDestroyed || _rime is null) return;
            _isFinalized = true;

            try
            {
                RimeNotificationBridge.OnEngineFinalizing(_rime);
                _rime.Finalize1();
            }
            finally
            {
                _rime = null;
                RimeEngineLifecycle.CompleteStop();
            }
        }
    }
}

internal static class RimeEngineLifecycle
{
    private static int s_isActive;

    internal static bool TryBeginStart()
        => Interlocked.CompareExchange(ref s_isActive, 1, 0) == 0;

    internal static void CompleteStop()
        => Volatile.Write(ref s_isActive, 0);
}
