namespace NanoAgent.CLI;

public static partial class Program
{
    private readonly record struct ConversationLine(
        string Markup,
        string Plain);

    private readonly record struct InlineRenderResult(
        string Markup,
        string Plain);

    private readonly record struct InputRenderLine(
        string Text,
        int? CursorColumn);

    private readonly record struct InputDisplayText(
        string Text,
        int CursorIndex,
        bool HasCollapsedPastes);

    private readonly record struct MarkdownFragment(
        string Text,
        string Style);

    private readonly record struct SlashCommandSuggestion(
        string Command,
        string Usage,
        string Description,
        bool RequiresArgument);
}
