namespace RimeSharp.PowerShell;

/// <summary>
/// Contains the atomic managed result of one native key event.
/// </summary>
public sealed class RimeKeyEventResult
{
    public bool Handled { get; }
    public string? Commit { get; }
    public RimeStatusSnapshot Status { get; }
    public RimeContextSnapshot Context { get; }
    public string Input { get; }
    public RimeNotification[] Notifications { get; }

    internal RimeKeyEventResult(
        bool handled,
        string? commit,
        RimeStatusSnapshot status,
        RimeContextSnapshot context,
        string input,
        RimeNotification[] notifications)
    {
        Handled = handled;
        Commit = commit;
        Status = status;
        Context = context;
        Input = input;
        Notifications = notifications;
    }
}

/// <summary>
/// Contains the final managed result of one native key-sequence simulation.
/// </summary>
public sealed class RimeKeySequenceResult
{
    public string? Commit { get; }
    public RimeStatusSnapshot Status { get; }
    public RimeContextSnapshot Context { get; }
    public string Input { get; }
    public RimeNotification[] Notifications { get; }

    internal RimeKeySequenceResult(
        string? commit,
        RimeStatusSnapshot status,
        RimeContextSnapshot context,
        string input,
        RimeNotification[] notifications)
    {
        Commit = commit;
        Status = status;
        Context = context;
        Input = input;
        Notifications = notifications;
    }
}
