namespace RimeSharp.PowerShell;

/// <summary>
/// Wraps a native RIME session ID for pipeline binding.
/// Not constructable by user code — created by <c>Start-Rime</c>.
/// </summary>
public sealed class RimeSession
{
    internal UIntPtr Id { get; }

    internal RimeSession(UIntPtr id)
    {
        Id = id;
    }
}
