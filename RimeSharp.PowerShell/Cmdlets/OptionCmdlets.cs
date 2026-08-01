using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Get the value of a boolean option for one session.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeOption")]
[OutputType(typeof(bool))]
public sealed class GetRimeOptionCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    protected override void ProcessRecord()
    {
        bool value;
        using (AcquireNativeGate())
        {
            var session = RimeProcessRuntime.RequireSession(this, Session);
            var rime = RimeProcessRuntime.ValidateSession(this, session);
            ThrowIfStopping();
            value = rime.GetOption(session.NativeId, Name);
            ThrowIfStopping();
        }

        WriteObject(value);
    }
}

/// <summary>
/// Set the value of a boolean option for one session.
/// </summary>
[Cmdlet(VerbsCommon.Set, "RimeOption")]
[OutputType(typeof(void))]
public sealed class SetRimeOptionCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    [Parameter(Mandatory = true)]
    public bool Value { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();
        rime.SetOption(session.NativeId, Name, Value);
        ThrowIfStopping();
    }
}
