using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Get the display label for a RIME option state.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeStateLabel")]
[OutputType(typeof(string))]
public sealed class GetRimeStateLabelCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("OptionName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("OptionState")]
    public bool State { get; set; }

    [Parameter]
    public SwitchParameter Abbreviated { get; set; }

    protected override void ProcessRecord()
    {
        string label;
        using (AcquireNativeGate())
        {
            var session = RimeProcessRuntime.RequireSession(this, Session);
            var rime = RimeProcessRuntime.ValidateSession(this, session);
            ThrowIfStopping();
            label = rime.GetStateLabel(
                session.NativeId,
                Name,
                State,
                Abbreviated);
            ThrowIfStopping();
        }

        WriteObject(label);
    }
}
