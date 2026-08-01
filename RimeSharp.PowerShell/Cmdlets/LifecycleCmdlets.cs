using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Start the process-wide RIME lifecycle without creating a session.
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "Rime")]
[OutputType(typeof(void))]
public sealed class StartRimeCmdlet : RimeTraitsCmdlet
{
    [Parameter]
    public SwitchParameter FullMaintenance { get; set; }

    protected override void BeginProcessing()
    {
        using var gate = AcquireNativeGate();
        var generation = RimeProcessRuntime.BeginStart(this);

        Rime? rime = null;
        RimeNotificationBridgeLifetime? bridge = null;
        var setupEntered = false;

        try
        {
            var traitsData = BuildTraits();
            ThrowIfStopping();

            rime = Rime.Instance();
            var traits = traitsData.CreateNativeTraits();

            setupEntered = true;
            rime.Setup(ref traits);
            ThrowIfStopping();

            bridge = RimeNotificationBridgeLifetime.CreateRegular();
            rime.SetNotificationHandler(bridge.Handler, bridge.ContextObject);
            ThrowIfStopping();

            rime.Initialize(ref traits);
            ThrowIfStopping();

            if (rime.StartMaintenance(FullMaintenance))
            {
                rime.JoinMaintenanceThread();
            }

            ThrowIfStopping();
            RimeProcessRuntime.CompleteStart(rime, bridge, generation);
        }
        catch (Exception primaryFailure)
        {
            var cleanupFailures = new List<Exception>();
            if (setupEntered && rime is not null)
            {
                try
                {
                    rime.Finalize1();
                }
                catch (Exception cleanupFailure)
                {
                    cleanupFailures.Add(cleanupFailure);
                }
            }

            RimeLifecycleState finalState;
            if (cleanupFailures.Count == 0)
            {
                bridge?.Release();
                RimeProcessRuntime.ResetInactive(invalidateSessions: true);
                finalState = RimeLifecycleState.Inactive;
            }
            else
            {
                RimeProcessRuntime.RetainFaultedLifecycle(rime!, bridge);
                finalState = RimeLifecycleState.Faulted;
            }

            var diagnostic = new RimeLifecycleFailureInfo(
                RimeLifecycleOperation.Start,
                finalState,
                primaryFailure,
                cleanupFailures);
            RimeProcessRuntime.ThrowError(
                this,
                primaryFailure,
                "RimeLifecycleStartFailed",
                primaryFailure is OperationCanceledException
                    ? ErrorCategory.OperationStopped
                    : ErrorCategory.ResourceUnavailable,
                diagnostic);
        }
    }
}

/// <summary>
/// Create and register one explicit session in the active lifecycle.
/// </summary>
[Cmdlet(VerbsCommon.New, "RimeSession")]
[OutputType(typeof(RimeSession))]
public sealed class NewRimeSessionCmdlet : RimeCmdlet
{
    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var rime = RimeProcessRuntime.RequireActive(this);
        ThrowIfStopping();

        UIntPtr nativeId;
        try
        {
            nativeId = rime.CreateSession();
        }
        catch (Exception ex)
        {
            RimeProcessRuntime.ThrowError(
                this,
                ex,
                "RimeSessionCreationFailed",
                ErrorCategory.ResourceUnavailable,
                null);
            return;
        }

        if (nativeId == UIntPtr.Zero)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new InvalidOperationException("librime did not create a session."),
                "RimeSessionCreationFailed",
                ErrorCategory.ResourceUnavailable,
                null);
            return;
        }

        RimeSession? session = null;
        try
        {
            session = RimeProcessRuntime.RegisterSession(nativeId);
            ThrowIfStopping();
            WriteObject(session, enumerateCollection: false);
        }
        catch (Exception primaryFailure)
        {
            var cleanupFailures = new List<Exception>();
            var destroyed = false;
            try
            {
                destroyed = rime.DestroySession(nativeId);
                if (!destroyed)
                {
                    cleanupFailures.Add(new InvalidOperationException(
                        $"librime did not destroy session {nativeId.ToUInt64()} during rollback."));
                }
            }
            catch (Exception cleanupFailure)
            {
                cleanupFailures.Add(cleanupFailure);
            }

            if (destroyed && session is not null)
            {
                RimeProcessRuntime.RemoveRegisteredSession(session);
            }

            if (cleanupFailures.Count != 0)
            {
                RimeProcessRuntime.RetainFaultedLifecycle(
                    rime,
                    RimeProcessRuntime.Bridge);
                var diagnostic = new RimeLifecycleFailureInfo(
                    RimeLifecycleOperation.NewSessionRollback,
                    RimeLifecycleState.Faulted,
                    primaryFailure,
                    cleanupFailures);
                RimeProcessRuntime.ThrowError(
                    this,
                    primaryFailure,
                    "RimeSessionCreationFailed",
                    primaryFailure is OperationCanceledException
                        ? ErrorCategory.OperationStopped
                        : ErrorCategory.ResourceUnavailable,
                    diagnostic);
                return;
            }

            if (primaryFailure is OperationCanceledException)
            {
                throw;
            }

            RimeProcessRuntime.ThrowError(
                this,
                primaryFailure,
                "RimeSessionCreationFailed",
                ErrorCategory.ResourceUnavailable,
                session ?? (object)nativeId.ToUInt64());
            return;
        }

    }
}

/// <summary>
/// Destroy one explicit session without changing the process lifecycle.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "RimeSession")]
[OutputType(typeof(void))]
public sealed class RemoveRimeSessionCmdlet : RimeCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        if (RimeProcessRuntime.IsGenuineDestroyedSession(session)) return;

        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        bool destroyed;
        try
        {
            destroyed = rime.DestroySession(session.NativeId);
        }
        catch (Exception ex)
        {
            RimeProcessRuntime.ThrowError(
                this,
                ex,
                "RimeSessionRemovalFailed",
                ErrorCategory.ResourceUnavailable,
                session);
            return;
        }

        RimeProcessRuntime.RemoveRegisteredSession(session);
        ThrowIfStopping();
        if (!destroyed)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new InvalidOperationException(
                    $"librime did not destroy session {session.Id}."),
                "RimeSessionRemovalFailed",
                ErrorCategory.ResourceUnavailable,
                session);
        }
    }
}

/// <summary>
/// Destroy all registered sessions and finalize the process-wide lifecycle.
/// </summary>
[Cmdlet(
    VerbsLifecycle.Stop,
    "Rime",
    SupportsShouldProcess = true,
    ConfirmImpact = ConfirmImpact.Medium)]
[OutputType(typeof(void))]
public sealed class StopRimeCmdlet : RimeCmdlet
{
    protected override void EndProcessing()
    {
        using var gate = AcquireNativeGate();
        var state = RimeProcessRuntime.State;
        if (state == RimeLifecycleState.Inactive) return;
        if (state != RimeLifecycleState.Active && state != RimeLifecycleState.Faulted)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new InvalidOperationException(
                    $"The RIME lifecycle cannot be stopped from state {state}."),
                "RimeLifecycleStopFailed",
                ErrorCategory.InvalidOperation,
                state);
            return;
        }

        if (!ShouldProcess("process RIME lifecycle", "Stop")) return;
        ThrowIfStopping();

        var rime = RimeProcessRuntime.Rime;
        if (rime is null)
        {
            var missingRuntime = new InvalidOperationException(
                "The lifecycle cleanup state does not contain a native RIME API instance.");
            RimeProcessRuntime.RetainFaultedLifecycle(null, RimeProcessRuntime.Bridge);
            var missingDiagnostic = new RimeLifecycleFailureInfo(
                RimeLifecycleOperation.Stop,
                RimeLifecycleState.Faulted,
                missingRuntime,
                Array.Empty<Exception>());
            RimeProcessRuntime.ThrowError(
                this,
                missingRuntime,
                "RimeLifecycleStopFailed",
                ErrorCategory.InvalidData,
                missingDiagnostic);
            return;
        }

        RimeProcessRuntime.BeginStopping();
        var cleanupFailures = new List<Exception>();

        foreach (var session in RimeProcessRuntime.Sessions.ToArray())
        {
            try
            {
                if (rime.DestroySession(session.NativeId))
                {
                    RimeProcessRuntime.RemoveRegisteredSession(session);
                }
                else
                {
                    cleanupFailures.Add(new InvalidOperationException(
                        $"librime did not destroy session {session.Id}."));
                }
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }
        }

        var finalized = false;
        try
        {
            rime.Finalize1();
            finalized = true;
        }
        catch (Exception ex)
        {
            cleanupFailures.Add(ex);
        }

        if (finalized)
        {
            RimeProcessRuntime.ResetInactive(invalidateSessions: true);
        }
        else
        {
            RimeProcessRuntime.RetainFaultedLifecycle(
                rime,
                RimeProcessRuntime.Bridge);
        }

        var cancellationFailure = StopToken.IsCancellationRequested
            ? new OperationCanceledException("Stop-Rime was stopped.", StopToken)
            : null;
        if (cancellationFailure is null && cleanupFailures.Count == 0) return;

        Exception primaryFailure;
        IReadOnlyList<Exception> laterFailures;
        if (cancellationFailure is not null)
        {
            primaryFailure = cancellationFailure;
            laterFailures = cleanupFailures;
        }
        else
        {
            primaryFailure = cleanupFailures[0];
            laterFailures = cleanupFailures.Skip(1).ToArray();
        }

        var diagnostic = new RimeLifecycleFailureInfo(
            RimeLifecycleOperation.Stop,
            RimeProcessRuntime.State,
            primaryFailure,
            laterFailures);
        RimeProcessRuntime.ThrowError(
            this,
            primaryFailure,
            "RimeLifecycleStopFailed",
            primaryFailure is OperationCanceledException
                ? ErrorCategory.OperationStopped
                : ErrorCategory.ResourceUnavailable,
            diagnostic);
    }
}
