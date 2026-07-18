namespace RimeSharp.PowerShell;

/// <summary>
/// Describes an available RIME schema and whether it is active for the session.
/// </summary>
public sealed class RimeSchemaInfo
{
    public string SchemaId { get; }
    public string Name { get; }
    public bool IsCurrent { get; }

    internal RimeSchemaInfo(string schemaId, string name, bool isCurrent)
    {
        SchemaId = schemaId;
        Name = name;
        IsCurrent = isCurrent;
    }
}
