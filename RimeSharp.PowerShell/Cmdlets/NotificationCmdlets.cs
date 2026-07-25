using System.Collections.Concurrent;
using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Enable native RIME notifications and queue them for PowerShell-safe delivery.
/// Register before Start-Rime to receive initialization and deployment messages.
/// </summary>
[Cmdlet(VerbsLifecycle.Register, "RimeNotification")]
[OutputType(typeof(void))]
public sealed class RegisterRimeNotificationCmdlet : PSCmdlet
{
    protected override void ProcessRecord()
    {
        RimeNotificationBridge.Register();
    }
}

/// <summary>
/// Dequeue all pending native RIME notifications.
/// </summary>
[Cmdlet(VerbsCommunications.Receive, "RimeNotification")]
[OutputType(typeof(RimeNotification))]
public sealed class ReceiveRimeNotificationCmdlet : PSCmdlet
{
    protected override void ProcessRecord()
    {
        while (RimeNotificationBridge.TryDequeue(out var notification))
        {
            if (notification is not null)
            {
                WriteObject(notification);
            }
        }
    }
}

internal static class RimeNotificationBridge
{
    private static readonly object s_syncRoot = new();
    private static readonly ConcurrentQueue<RimeNotification> s_notifications = new();
    private static readonly RimeNotificationHandler s_handler = OnNotification;

    private static bool s_registered;
    private static Rime? s_rime;

    internal static void Register()
    {
        lock (s_syncRoot)
        {
            s_registered = true;
            s_rime?.SetNotificationHandler(s_handler);
        }
    }

    internal static void OnEngineSetup(Rime rime)
    {
        lock (s_syncRoot)
        {
            while (s_notifications.TryDequeue(out _))
            {
            }

            s_rime = rime;
            if (s_registered)
            {
                rime.SetNotificationHandler(s_handler);
            }
        }
    }

    internal static void OnEngineFinalizing(Rime rime)
    {
        lock (s_syncRoot)
        {
            if (ReferenceEquals(s_rime, rime))
            {
                s_rime = null;
            }
        }
    }

    internal static bool TryDequeue(out RimeNotification? notification)
        => s_notifications.TryDequeue(out notification);

    private static void OnNotification(
        UIntPtr contextObject,
        UIntPtr sessionId,
        string messageType,
        string messageValue)
    {
        s_notifications.Enqueue(new RimeNotification(
            contextObject,
            sessionId,
            messageType ?? string.Empty,
            messageValue ?? string.Empty));
    }
}
