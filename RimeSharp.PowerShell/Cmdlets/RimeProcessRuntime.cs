using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Threading;

namespace RimeSharp.PowerShell.Cmdlets;

internal static class RimeProcessRuntime
{
    private static readonly SemaphoreSlim s_nativeGate = new(1, 1);
    private static readonly Dictionary<ulong, RimeSession> s_sessions = [];

    private static RimeLifecycleState s_state = RimeLifecycleState.Inactive;
    private static object? s_generation;
    private static Rime? s_rime;
    private static RimeNotificationBridgeLifetime? s_bridge;

    internal static object SessionOwnerToken { get; } = new();

    internal static RimeLifecycleState State => s_state;
    internal static Rime? Rime => s_rime;
    internal static RimeNotificationBridgeLifetime? Bridge => s_bridge;
    internal static IReadOnlyCollection<RimeSession> Sessions => s_sessions.Values;

    internal static RimeNativeGateLease AcquireNativeGate(CancellationToken cancellationToken)
    {
        s_nativeGate.Wait(cancellationToken);
        return new RimeNativeGateLease(s_nativeGate);
    }

    internal static object BeginStart(PSCmdlet cmdlet)
    {
        switch (s_state)
        {
            case RimeLifecycleState.Inactive:
                break;
            case RimeLifecycleState.Faulted:
                ThrowLifecycleFaulted(cmdlet);
                break;
            default:
                ThrowError(
                    cmdlet,
                    new InvalidOperationException(
                        "A RIME lifecycle is already active or changing state in this process."),
                    "RimeLifecycleAlreadyActive",
                    ErrorCategory.ResourceExists,
                    s_state);
                break;
        }

        s_state = RimeLifecycleState.Starting;
        s_generation = new object();
        s_sessions.Clear();
        RimeNotificationQueue.Clear();
        return s_generation;
    }

    internal static void CompleteStart(
        Rime rime,
        RimeNotificationBridgeLifetime bridge,
        object generation)
    {
        s_rime = rime;
        s_bridge = bridge;
        s_generation = generation;
        bridge.CompleteStartup();
        s_state = RimeLifecycleState.Active;
    }

    internal static void RetainFaultedLifecycle(
        Rime? rime,
        RimeNotificationBridgeLifetime? bridge)
    {
        s_rime = rime;
        s_bridge = bridge;
        s_state = RimeLifecycleState.Faulted;
    }

    internal static void ResetInactive(bool invalidateSessions)
    {
        if (invalidateSessions)
        {
            foreach (var session in s_sessions.Values)
            {
                session.MarkDestroyed();
            }
        }

        s_sessions.Clear();
        s_generation = null;
        s_rime = null;
        s_bridge?.Release();
        s_bridge = null;
        s_state = RimeLifecycleState.Inactive;
    }

    internal static void BeginStopping()
        => s_state = RimeLifecycleState.Stopping;

    internal static RimeSession RegisterSession(UIntPtr nativeId)
    {
        if (s_generation is null)
        {
            throw new InvalidOperationException("The active lifecycle has no generation token.");
        }

        var session = new RimeSession(nativeId, s_generation, SessionOwnerToken);
        s_sessions.Add(session.Id, session);
        return session;
    }

    internal static void RemoveRegisteredSession(RimeSession session)
    {
        if (s_sessions.TryGetValue(session.Id, out var registered)
            && ReferenceEquals(registered, session))
        {
            s_sessions.Remove(session.Id);
        }

        session.MarkDestroyed();
    }

    internal static bool IsGenuineDestroyedSession(RimeSession session)
        => session.IsOwnedBy(SessionOwnerToken) && session.IsDestroyed;

    internal static Rime ValidateSession(PSCmdlet cmdlet, RimeSession session)
    {
        var rime = RequireActive(cmdlet);
        if (!session.IsOwnedBy(SessionOwnerToken)
            || session.IsDestroyed
            || s_generation is null
            || !session.IsFromGeneration(s_generation)
            || !s_sessions.TryGetValue(session.Id, out var registered)
            || !ReferenceEquals(registered, session)
            || !ReferenceEquals(registered.IdentityToken, session.IdentityToken)
            || !rime.FindSession(session.NativeId))
        {
            ThrowError(
                cmdlet,
                new ArgumentException(
                    $"Session {session.Id} is not a live module-owned session.",
                    nameof(session)),
                "RimeSessionInvalid",
                ErrorCategory.InvalidArgument,
                session);
        }

        return rime;
    }

    internal static Rime RequireActive(PSCmdlet cmdlet)
    {
        if (s_state == RimeLifecycleState.Faulted)
        {
            ThrowLifecycleFaulted(cmdlet);
        }

        if (s_state != RimeLifecycleState.Active || s_rime is null)
        {
            ThrowError(
                cmdlet,
                new InvalidOperationException("No active RIME lifecycle exists in this process."),
                "RimeLifecycleInactive",
                ErrorCategory.InvalidOperation,
                s_state);
        }

        return s_rime!;
    }

    internal static void EnsureInactiveForDeployment(PSCmdlet cmdlet)
    {
        if (s_state == RimeLifecycleState.Faulted)
        {
            ThrowLifecycleFaulted(cmdlet);
        }

        if (s_state != RimeLifecycleState.Inactive)
        {
            ThrowError(
                cmdlet,
                new InvalidOperationException(
                    "Deployment requires the process RIME lifecycle to be inactive."),
                "RimeDeploymentWhileActive",
                ErrorCategory.ResourceBusy,
                s_state);
        }
    }

    internal static void BeginDeployment(PSCmdlet cmdlet)
    {
        EnsureInactiveForDeployment(cmdlet);
        s_state = RimeLifecycleState.Starting;
        s_generation = null;
        s_sessions.Clear();
    }

    internal static RimeSession RequireSession(PSCmdlet cmdlet, RimeSession? session)
    {
        if (session is null)
        {
            ThrowError(
                cmdlet,
                new InvalidOperationException("An explicit RIME session is required."),
                "RimeSessionMissing",
                ErrorCategory.InvalidArgument,
                null);
        }

        return session!;
    }

    internal static void ThrowLifecycleFaulted(PSCmdlet cmdlet)
    {
        ThrowError(
            cmdlet,
            new InvalidOperationException(
                "The process RIME lifecycle requires cleanup. Run Stop-Rime before retrying."),
            "RimeLifecycleFaulted",
            ErrorCategory.InvalidOperation,
            s_state);
    }

    internal static void ThrowError(
        PSCmdlet cmdlet,
        Exception exception,
        string errorId,
        ErrorCategory category,
        object? target)
    {
        cmdlet.ThrowTerminatingError(new ErrorRecord(exception, errorId, category, target));
    }
}

internal sealed class RimeNotificationBridgeLifetime
{
    private readonly object _syncRoot = new();
    private readonly RimeNotificationRoute _route;
    private readonly List<RimeNotification> _deploymentNotifications = [];
    private readonly GCHandle _contextHandle;

    private RimeNotificationCapture? _capture;
    private bool _forwardStartup;
    private bool _released;

    internal RimeNotificationHandler Handler { get; }
    internal UIntPtr ContextObject { get; }

    private RimeNotificationBridgeLifetime(RimeNotificationRoute route)
    {
        _route = route;
        _forwardStartup = route == RimeNotificationRoute.Regular;
        Handler = OnNativeNotification;
        _contextHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        var pointer = GCHandle.ToIntPtr(_contextHandle);
        ContextObject = new UIntPtr(unchecked((ulong)pointer.ToInt64()));
    }

    internal static RimeNotificationBridgeLifetime CreateRegular()
        => new(RimeNotificationRoute.Regular);

    internal static RimeNotificationBridgeLifetime CreateDeployment()
        => new(RimeNotificationRoute.Deployment);

    internal void CompleteStartup()
    {
        lock (_syncRoot)
        {
            _forwardStartup = false;
        }
    }

    internal RimeNotificationCapture BeginCapture(RimeSession session)
    {
        lock (_syncRoot)
        {
            if (_capture is not null)
            {
                throw new InvalidOperationException("A notification capture is already active.");
            }

            _capture = new RimeNotificationCapture(session);
            return _capture;
        }
    }

    internal RimeNotification[] EndCapture(RimeNotificationCapture capture)
    {
        lock (_syncRoot)
        {
            if (!ReferenceEquals(_capture, capture))
            {
                throw new InvalidOperationException("The notification capture is not active.");
            }

            _capture = null;
            return capture.Notifications.ToArray();
        }
    }

    internal RimeNotification[] GetDeploymentNotifications()
    {
        lock (_syncRoot)
        {
            return _deploymentNotifications.ToArray();
        }
    }

    internal void Release()
    {
        lock (_syncRoot)
        {
            if (_released) return;

            _released = true;
            _capture = null;
            if (_contextHandle.IsAllocated)
            {
                _contextHandle.Free();
            }
        }
    }

    private static void OnNativeNotification(
        UIntPtr contextObject,
        UIntPtr sessionId,
        string messageType,
        string messageValue)
    {
        try
        {
            var pointer = new IntPtr(unchecked((long)contextObject.ToUInt64()));
            var handle = GCHandle.FromIntPtr(pointer);
            if (handle.Target is RimeNotificationBridgeLifetime bridge)
            {
                bridge.Route(new RimeNotification(
                    sessionId.ToUInt64(),
                    messageType ?? string.Empty,
                    messageValue ?? string.Empty));
            }
        }
        catch
        {
            // Exceptions must never cross the native callback boundary.
        }
    }

    private void Route(RimeNotification notification)
    {
        RimeNotificationOrigin? forwardingOrigin = null;
        lock (_syncRoot)
        {
            if (_released) return;

            if (_route == RimeNotificationRoute.Deployment)
            {
                _deploymentNotifications.Add(notification);
                forwardingOrigin = RimeNotificationOrigin.Deployment;
            }
            else if (_capture is not null
                && notification.SessionId == _capture.Session.Id)
            {
                _capture.Notifications.Add(notification);
            }
            else
            {
                RimeNotificationQueue.Enqueue(notification);
                if (_forwardStartup)
                {
                    forwardingOrigin = RimeNotificationOrigin.Startup;
                }
            }
        }

        if (forwardingOrigin.HasValue)
        {
            RimeNotificationSource.Instance.Publish(
                new RimeNotificationEventArgs(forwardingOrigin.Value, notification));
        }
    }
}

internal sealed class RimeNotificationCapture
{
    internal RimeSession Session { get; }
    internal List<RimeNotification> Notifications { get; } = [];

    internal RimeNotificationCapture(RimeSession session)
    {
        Session = session;
    }
}

internal static class RimeNotificationQueue
{
    private static readonly object s_syncRoot = new();
    private static readonly Queue<RimeNotification> s_queue = new();

    internal static void Enqueue(RimeNotification notification)
    {
        lock (s_syncRoot)
        {
            s_queue.Enqueue(notification);
        }
    }

    internal static RimeNotification[] Drain()
    {
        lock (s_syncRoot)
        {
            var notifications = s_queue.ToArray();
            s_queue.Clear();
            return notifications;
        }
    }

    internal static void Clear()
    {
        lock (s_syncRoot)
        {
            s_queue.Clear();
        }
    }
}

internal enum RimeNotificationRoute
{
    Regular,
    Deployment,
}
