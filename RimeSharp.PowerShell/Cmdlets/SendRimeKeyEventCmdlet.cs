using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Send a single raw key event (key code + modifier mask) to a RIME session.
/// Uses <c>ProcessKey</c> for low-level key handling.
/// </summary>
[Cmdlet(VerbsCommunications.Send, "RimeKeyEvent")]
[OutputType(typeof(bool))]
public sealed class SendRimeKeyEventCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true,
        ValueFromPipeline = true
    )]
    public RimeSession Session { get; set; } = null!;

    [Parameter(
        Position = 1,
        Mandatory = true
    )]
    public int KeyCode { get; set; }

    [Parameter(Position = 2)]
    public int Mask { get; set; }

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();
    }

    protected override void ProcessRecord()
    {
        if (_rime is null) return;

        var handled = _rime.ProcessKey(Session.Id, KeyCode, Mask);
        WriteObject(handled);
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
