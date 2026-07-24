namespace RimeSharp.PowerShell;

/// <summary>
/// Describes a schema listed in the RIME switcher settings.
/// </summary>
public sealed class RimeSwitcherSchemaInfo
{
    public string SchemaId { get; }
    public string Name { get; }

    internal RimeSwitcherSchemaInfo(string schemaId, string name)
    {
        SchemaId = schemaId;
        Name = name;
    }
}
