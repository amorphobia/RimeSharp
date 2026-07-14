using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Simulate a key sequence (e.g. "nihao" or "Down") and return a
/// <see cref="RimeResponse"/> containing commit, status, and context.
/// Internally calls <c>SimulateKeySequence</c>.
/// </summary>
[Cmdlet(VerbsCommunications.Send, "RimeKey")]
[OutputType(typeof(RimeResponse))]
public sealed class SendRimeKeyCmdlet : PSCmdlet, IDisposable
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
    public string Sequence { get; set; } = "";

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();
    }

    protected override void ProcessRecord()
    {
        if (_rime is null) return;

        _rime.SimulateKeySequence(Session.Id, Sequence);

        var commit  = _rime.GetCommit(Session.Id);
        var status  = _rime.GetStatus(Session.Id);
        var context = _rime.GetContext(Session.Id);

        var response = new RimeResponse(commit, status, context);
        WriteObject(response);
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
