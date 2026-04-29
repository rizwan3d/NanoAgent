namespace NanoAgent.CLI;

public sealed class CollapsedInputPaste
{
    public CollapsedInputPaste(int startIndex, int length, int lineCount)
    {
        StartIndex = startIndex;
        Length = length;
        LineCount = lineCount;
    }

    public int StartIndex { get; set; }

    public int Length { get; set; }

    public int LineCount { get; set; }

    public int EndIndex => StartIndex + Length;
}
