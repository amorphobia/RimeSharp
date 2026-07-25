namespace RimeSharp.PowerShell;

/// <summary>
/// Composite result returned by <c>Send-RimeKey</c> containing all render-relevant
/// state after processing a key sequence. The response is fully managed and owns
/// no native resources.
/// </summary>
public sealed class RimeResponse
{
    /// <summary>Committed text, if any.</summary>
    public string? Commit { get; }

    /// <summary>Current engine status flags (schema, mode, composing…).</summary>
    public RimeStatusSnapshot Status { get; }

    /// <summary>Current input context (preedit, candidates, menu).</summary>
    public RimeContextSnapshot Context { get; }

    internal RimeResponse(
        string? commit,
        RimeStatusSnapshot status,
        RimeContextSnapshot context)
    {
        Commit = commit;
        Status = status;
        Context = context;
    }
}
