namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceFileReadResult
{
    public WorkspaceFileReadResult(
        string path,
        string content,
        int startLine,
        int endLine,
        int totalLines,
        bool truncated,
        int? nextOffset,
        string sha256,
        string encoding)
        : this(
            path,
            content,
            content,
            startLine,
            endLine,
            totalLines,
            truncated,
            nextOffset,
            sha256,
            encoding)
    {
    }

    public WorkspaceFileReadResult(
        string path,
        string rawContent,
        string displayContent,
        int startLine,
        int endLine,
        int totalLines,
        bool truncated,
        int? nextOffset,
        string sha256,
        string encoding)
    {
        Path = path;
        RawContent = rawContent;
        DisplayContent = displayContent;
        StartLine = startLine;
        EndLine = endLine;
        TotalLines = totalLines;
        Truncated = truncated;
        NextOffset = nextOffset;
        Sha256 = sha256;
        Encoding = encoding;
    }

    public string Path { get; init; }

    public string Content => RawContent;

    public string RawContent { get; init; }

    public string DisplayContent { get; init; }

    public int StartLine { get; init; }

    public int EndLine { get; init; }

    public int TotalLines { get; init; }

    public bool Truncated { get; init; }

    public int? NextOffset { get; init; }

    public string Sha256 { get; init; }

    public string Encoding { get; init; }
}
