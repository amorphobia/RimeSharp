using System.Diagnostics;

namespace RimeSharp.PowerShell;

/// <summary>
/// Identifies the long-running native operation that emitted a notification.
/// </summary>
public enum RimeNotificationOrigin
{
    Startup,
    Deployment,
}

/// <summary>
/// Carries a notification forwarded from a long-running native operation.
/// </summary>
public sealed class RimeNotificationEventArgs : EventArgs
{
    public RimeNotificationOrigin Origin { get; }
    public RimeNotification Notification { get; }

    internal RimeNotificationEventArgs(
        RimeNotificationOrigin origin,
        RimeNotification notification)
    {
        Origin = origin;
        Notification = notification;
    }
}

/// <summary>
/// Provides process-wide managed notification forwarding.
/// </summary>
public sealed class RimeNotificationSource
{
    private static readonly TraceSource s_trace =
        new("RimeSharp.PowerShell.NotificationDispatch");

    private readonly object _syncRoot = new();
    private readonly List<SubscriberLane> _subscribers = [];

    internal static RimeNotificationSource Instance { get; } = new();

    private RimeNotificationSource()
    {
    }

    public event EventHandler<RimeNotificationEventArgs>? NotificationReceived
    {
        add
        {
            if (value is null) return;

            lock (_syncRoot)
            {
                _subscribers.Add(new SubscriberLane(this, value));
            }
        }
        remove
        {
            if (value is null) return;

            SubscriberLane? removed = null;
            lock (_syncRoot)
            {
                for (var i = _subscribers.Count - 1; i >= 0; --i)
                {
                    if (_subscribers[i].Handler != value) continue;

                    removed = _subscribers[i];
                    _subscribers.RemoveAt(i);
                    break;
                }
            }

            removed?.Deactivate();
        }
    }

    internal void Publish(RimeNotificationEventArgs eventArgs)
    {
        SubscriberLane[] subscribers;
        lock (_syncRoot)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Enqueue(eventArgs);
        }
    }

    private sealed class SubscriberLane
    {
        private readonly RimeNotificationSource _source;
        private readonly object _syncRoot = new();
        private readonly Queue<RimeNotificationEventArgs> _queue = new();

        private bool _active = true;
        private bool _workerScheduled;

        internal EventHandler<RimeNotificationEventArgs> Handler { get; }

        internal SubscriberLane(
            RimeNotificationSource source,
            EventHandler<RimeNotificationEventArgs> handler)
        {
            _source = source;
            Handler = handler;
        }

        internal void Enqueue(RimeNotificationEventArgs eventArgs)
        {
            var scheduleWorker = false;
            lock (_syncRoot)
            {
                if (!_active) return;

                _queue.Enqueue(eventArgs);
                if (!_workerScheduled)
                {
                    _workerScheduled = true;
                    scheduleWorker = true;
                }
            }

            if (scheduleWorker)
            {
                ThreadPool.QueueUserWorkItem(_ => Dispatch());
            }
        }

        internal void Deactivate()
        {
            lock (_syncRoot)
            {
                _active = false;
                _queue.Clear();
            }
        }

        private void Dispatch()
        {
            while (true)
            {
                RimeNotificationEventArgs eventArgs;
                lock (_syncRoot)
                {
                    if (!_active || _queue.Count == 0)
                    {
                        _workerScheduled = false;
                        return;
                    }

                    eventArgs = _queue.Dequeue();
                }

                try
                {
                    Handler(_source, eventArgs);
                }
                catch (Exception ex)
                {
                    TraceSubscriberFailure(ex, eventArgs.Notification);
                }
            }
        }

        private void TraceSubscriberFailure(Exception exception, RimeNotification notification)
        {
            var method = Handler.Method;
            s_trace.TraceEvent(
                TraceEventType.Error,
                0,
                "Subscriber {0}.{1} failed for notification SessionId={2}, MessageType={3}: {4}",
                method.DeclaringType?.FullName ?? "<unknown>",
                method.Name,
                notification.SessionId,
                notification.MessageType,
                exception);
            s_trace.Flush();
        }
    }
}
