using Spectre.Console;

namespace StemCode.CLI;

internal static class CliBranding
{
    private static readonly (string Stem, string Code)[] Wordmark =
    [
        (
            "███████╗ ████████╗ ███████╗ ███╗   ███╗",
            "  ██████╗  ██████╗  ██████╗  ███████╗"
        ),
        (
            "██╔════╝ ╚══██╔══╝ ██╔════╝ ████╗ ████║",
            " ██╔════╝ ██╔═══██╗ ██╔══██╗ ██╔════╝"
        ),
        (
            "███████╗    ██║    █████╗   ██╔████╔██║",
            " ██║      ██║   ██║ ██║  ██║ █████╗"
        ),
        (
            "╚════██║    ██║    ██╔══╝   ██║╚██╔╝██║",
            " ██║      ██║   ██║ ██║  ██║ ██╔══╝"
        ),
        (
            "███████║    ██║    ███████╗ ██║ ╚═╝ ██║",
            " ╚██████╗ ╚██████╔╝ ██████╔╝ ███████╗"
        ),
        (
            "╚══════╝    ╚═╝    ╚══════╝ ╚═╝     ╚═╝",
            "  ╚═════╝  ╚═════╝  ╚═════╝  ╚══════╝"
        )
    ];

    internal static string BuildHeaderBodyMarkup()
    {
        List<string> lines = [];

        lines.Add("[grey]  [/]");

        foreach ((string stem, string code) in Wordmark)
        {
            lines.Add(
                $"[grey]  [/]" +
                $"[white]{Markup.Escape(stem)}[/]" +
                $"[aqua]{Markup.Escape(code)}[/]");
        }

        lines.Add("[grey]  [/]");

        return string.Join('\n', lines);
    }
}