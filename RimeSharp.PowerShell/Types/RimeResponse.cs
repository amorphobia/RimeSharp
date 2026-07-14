namespace RimeSharp.PowerShell;

/// <summary>
/// Composite result returned by <c>Send-RimeKey</c> containing all render-relevant
/// state after processing a key sequence.
/// </summary>
public sealed class RimeResponse : IDisposable
{
    private readonly RimeCommit _commit;
    private readonly RimeStatus _status;
    private readonly RimeContext _context;
    private bool _disposed;

    /// <summary>Committed text, if any.</summary>
    public string? Commit => _commit.Text;

    /// <summary>Current engine status flags (schema, mode, composing…).</summary>
    public RimeStatus Status => _status;

    /// <summary>Current input context (preedit, candidates, menu).</summary>
    public RimeContext Context => _context;

    internal RimeResponse(RimeCommit commit, RimeStatus status, RimeContext context)
    {
        _commit = commit;
        _status = status;
        _context = context;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commit.Dispose();
        _status.Dispose();
        _context.Dispose();
    }
}
