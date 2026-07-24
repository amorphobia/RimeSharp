namespace RimeSharp.PowerShell;

/// <summary>
/// Describes a RIME candidate and its global index.
/// </summary>
public sealed class RimeCandidateInfo
{
    public int Index { get; }
    public string Text { get; }
    public string? Comment { get; }

    internal RimeCandidateInfo(int index, RimeCandidate candidate)
    {
        Index = index;
        Text = candidate.Text;
        Comment = candidate.Comment;
    }
}
