namespace RimeSharp.PowerShell;

/// <summary>
/// Managed snapshot of a RIME composition.
/// </summary>
public sealed class RimeCompositionSnapshot
{
    public int Length { get; }
    public int CursorPos { get; }
    public int SelStart { get; }
    public int SelEnd { get; }
    public string? Preedit { get; }

    internal RimeCompositionSnapshot(RimeComposition composition)
    {
        Length = composition.Length;
        CursorPos = composition.CursorPos;
        SelStart = composition.SelStart;
        SelEnd = composition.SelEnd;
        Preedit = composition.Preedit;
    }
}

/// <summary>
/// Managed snapshot of a RIME candidate.
/// </summary>
public sealed class RimeCandidateSnapshot
{
    public string Text { get; }
    public string? Comment { get; }

    internal RimeCandidateSnapshot(RimeCandidate candidate)
    {
        Text = candidate.Text;
        Comment = candidate.Comment;
    }
}

/// <summary>
/// Managed snapshot of a RIME candidate menu.
/// </summary>
public sealed class RimeMenuSnapshot
{
    public int PageSize { get; }
    public int PageNo { get; }
    public bool IsLastPage { get; }
    public int HighlightedCandidateIndex { get; }
    public int NumCandidates { get; }
    public string SelectKeys { get; }
    public RimeCandidateSnapshot[] Candidates { get; }

    internal RimeMenuSnapshot(RimeMenu menu)
    {
        PageSize = menu.PageSize;
        PageNo = menu.PageNo;
        IsLastPage = menu.IsLastPage;
        HighlightedCandidateIndex = menu.HighlightedCandidateIndex;
        NumCandidates = menu.NumCandidates;
        SelectKeys = menu.SelectKeys;

        var candidates = menu.Candidates;
        Candidates = new RimeCandidateSnapshot[candidates.Length];
        for (var i = 0; i < candidates.Length; ++i)
        {
            Candidates[i] = new RimeCandidateSnapshot(candidates[i]);
        }
    }
}

/// <summary>
/// Managed snapshot of a RIME input context.
/// </summary>
public sealed class RimeContextSnapshot
{
    public RimeCompositionSnapshot Composition { get; }
    public RimeMenuSnapshot Menu { get; }
    public string? CommitTextPreview { get; }
    public string?[] SelectLabels { get; }

    internal RimeContextSnapshot(RimeContext context)
    {
        Composition = new RimeCompositionSnapshot(context.Composition);
        Menu = new RimeMenuSnapshot(context.Menu);
        CommitTextPreview = context.CommitTextPreview;
        SelectLabels = context.SelectLabels;
    }
}

/// <summary>
/// Managed snapshot of RIME engine status.
/// </summary>
public sealed class RimeStatusSnapshot
{
    public string? SchemaId { get; }
    public string? SchemaName { get; }
    public bool IsDisabled { get; }
    public bool IsComposing { get; }
    public bool IsAsciiMode { get; }
    public bool IsFullShape { get; }
    public bool IsSimplified { get; }
    public bool IsTraditional { get; }
    public bool IsAsciiPunct { get; }

    internal RimeStatusSnapshot(RimeStatus status)
    {
        SchemaId = status.SchemaId;
        SchemaName = status.SchemaName;
        IsDisabled = status.IsDisabled;
        IsComposing = status.IsComposing;
        IsAsciiMode = status.IsAsciiMode;
        IsFullShape = status.IsFullShape;
        IsSimplified = status.IsSimplified;
        IsTraditional = status.IsTraditional;
        IsAsciiPunct = status.IsAsciiPunct;
    }
}
