namespace RimeSharp.PowerShell;

/// <summary>
/// Identifies a module-owned native RIME session.
/// </summary>
public sealed class RimeSession
{
    private readonly object _generationToken;
    private readonly object _ownerToken;
    private int _destroyed;

    public ulong Id { get; }

    internal object IdentityToken { get; } = new();
    internal UIntPtr NativeId => new UIntPtr(Id);
    internal bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

    internal RimeSession(UIntPtr id, object generationToken, object ownerToken)
    {
        Id = id.ToUInt64();
        _generationToken = generationToken;
        _ownerToken = ownerToken;
    }

    internal bool IsFromGeneration(object generationToken)
        => ReferenceEquals(_generationToken, generationToken);

    internal bool IsOwnedBy(object ownerToken)
        => ReferenceEquals(_ownerToken, ownerToken);

    internal void MarkDestroyed()
        => Interlocked.Exchange(ref _destroyed, 1);
}
