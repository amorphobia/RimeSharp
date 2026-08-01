using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Return the process-wide managed notification event source.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeNotificationSource")]
[OutputType(typeof(RimeNotificationSource))]
public sealed class GetRimeNotificationSourceCmdlet : PSCmdlet
{
    protected override void ProcessRecord()
        => WriteObject(RimeNotificationSource.Instance, enumerateCollection: false);
}

/// <summary>
/// Atomically dequeue the notifications pending at the start of this call.
/// </summary>
[Cmdlet(VerbsCommunications.Receive, "RimeNotification")]
[OutputType(typeof(RimeNotification))]
public sealed class ReceiveRimeNotificationCmdlet : PSCmdlet
{
    protected override void ProcessRecord()
    {
        foreach (var notification in RimeNotificationQueue.Drain())
        {
            WriteObject(notification, enumerateCollection: false);
        }
    }
}
