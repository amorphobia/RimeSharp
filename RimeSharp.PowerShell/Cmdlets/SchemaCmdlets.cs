using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// List available RIME schemas and identify the schema active for a session.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeSchema")]
[OutputType(typeof(RimeSchemaInfo))]
public sealed class GetRimeSchemaCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();
        Session ??= SessionState.PSVariable.GetValue("global:RimeDefaultSession") as RimeSession;
    }

    protected override void ProcessRecord()
    {
        if (_rime is null) return;
        if (Session is null)
        {
            var ex = new InvalidOperationException(
                "No RIME session specified. Pipe a session from Start-Rime or use -Session.");
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionMissing",
                ErrorCategory.InvalidOperation, null));
            return;
        }

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        using var status = _rime.GetStatus(Session.Id);
        var currentSchemaId = status.SchemaId;
        if (string.IsNullOrEmpty(currentSchemaId))
        {
            var ex = new ArgumentException(
                $"Session {Session.Id} is invalid or has no active schema.", nameof(Session));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeCurrentSchemaUnavailable",
                ErrorCategory.InvalidArgument, Session));
            return;
        }

        foreach (var schema in _rime.GetSchemaList())
        {
            WriteObject(new RimeSchemaInfo(
                schema.SchemaId,
                schema.Name,
                string.Equals(schema.SchemaId, currentSchemaId, StringComparison.Ordinal)));
        }
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}

/// <summary>
/// Select the active schema for a RIME session.
/// </summary>
[Cmdlet(VerbsCommon.Set, "RimeSchema")]
[OutputType(typeof(void))]
public sealed class SetRimeSchemaCmdlet : PSCmdlet, IDisposable
{
    private Rime? _rime;

    [Parameter(
        Position = 0,
        Mandatory = true
    )]
    [ValidateNotNullOrEmpty]
    public string SchemaId { get; set; } = "";

    [Parameter(
        Position = 1,
        ValueFromPipeline = true
    )]
    public RimeSession? Session { get; set; }

    protected override void BeginProcessing()
    {
        _rime = Rime.Instance();
        Session ??= SessionState.PSVariable.GetValue("global:RimeDefaultSession") as RimeSession;
    }

    protected override void ProcessRecord()
    {
        if (_rime is null) return;
        if (Session is null)
        {
            var ex = new InvalidOperationException(
                "No RIME session specified. Pipe a session from Start-Rime or use -Session.");
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionMissing",
                ErrorCategory.InvalidOperation, null));
            return;
        }

        SessionValidation.EnsureSessionValid(this, _rime, Session);
        if (!_rime.SelectSchema(Session.Id, SchemaId))
        {
            var ex = new ArgumentException(
                $"Schema '{SchemaId}' could not be selected.", nameof(SchemaId));
            ThrowTerminatingError(new ErrorRecord(ex, "RimeSchemaSelectionFailed",
                ErrorCategory.InvalidArgument, SchemaId));
        }
    }

    protected override void StopProcessing() => Dispose();
    public void Dispose() => _rime = null;
}
