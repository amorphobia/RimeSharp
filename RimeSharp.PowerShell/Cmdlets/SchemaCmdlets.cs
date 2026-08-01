using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// List available schemas and identify the schema active for one session.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeSchema")]
[OutputType(typeof(RimeSchemaInfo))]
public sealed class GetRimeSchemaCmdlet : RimeSessionCmdlet
{
    protected override void ProcessRecord()
    {
        RimeSchemaInfo[] result;
        using (AcquireNativeGate())
        {
            var session = RimeProcessRuntime.RequireSession(this, Session);
            var rime = RimeProcessRuntime.ValidateSession(this, session);
            ThrowIfStopping();

            using var status = rime.GetStatus(session.NativeId);
            var currentSchemaId = status.SchemaId;
            if (string.IsNullOrEmpty(currentSchemaId))
            {
                RimeProcessRuntime.ThrowError(
                    this,
                    new InvalidOperationException(
                        $"Session {session.Id} has no active schema."),
                    "RimeCurrentSchemaUnavailable",
                    ErrorCategory.InvalidData,
                    session);
                return;
            }

            var schemas = rime.GetSchemaList();
            result = new RimeSchemaInfo[schemas.Length];
            for (var i = 0; i < schemas.Length; ++i)
            {
                result[i] = new RimeSchemaInfo(
                    schemas[i].SchemaId,
                    schemas[i].Name,
                    string.Equals(
                        schemas[i].SchemaId,
                        currentSchemaId,
                        StringComparison.Ordinal));
            }

            ThrowIfStopping();
        }

        foreach (var schema in result)
        {
            WriteObject(schema, enumerateCollection: false);
        }
    }
}

/// <summary>
/// Select the active schema for one session.
/// </summary>
[Cmdlet(VerbsCommon.Set, "RimeSchema")]
[OutputType(typeof(void))]
public sealed class SetRimeSchemaCmdlet : RimeSessionCmdlet
{
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SchemaId { get; set; } = string.Empty;

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var session = RimeProcessRuntime.RequireSession(this, Session);
        var rime = RimeProcessRuntime.ValidateSession(this, session);
        ThrowIfStopping();

        var succeeded = rime.SelectSchema(session.NativeId, SchemaId);
        ThrowIfStopping();
        if (!succeeded)
        {
            RimeProcessRuntime.ThrowError(
                this,
                new ArgumentException(
                    $"Schema '{SchemaId}' could not be selected.",
                    nameof(SchemaId)),
                "RimeSchemaSelectionFailed",
                ErrorCategory.InvalidArgument,
                SchemaId);
        }
    }
}
