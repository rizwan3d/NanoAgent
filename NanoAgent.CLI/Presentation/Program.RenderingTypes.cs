namespace NanoAgent.CLI;

public static partial class Program
{
    private readonly record struct ConversationLine(
        string Markup,
        string Plain);

    private readonly record struct InlineRenderResult(
        string Markup,
        string Plain);

    private readonly record struct MarkdownFragment(
        string Text,
        string Style);
}
