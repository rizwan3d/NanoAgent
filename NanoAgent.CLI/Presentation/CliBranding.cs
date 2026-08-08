using Spectre.Console;

namespace NanoAgent.CLI;

internal static class CliBranding
{
    private static readonly (string Nano, string Agent)[] Wordmark =
    [
        (
            "███╗   ██╗  █████╗  ███╗   ██╗  ██████╗",
            "  █████╗   ██████╗   ███████╗  ███╗   ██╗  ████████╗"
        ),
        (
            "████╗  ██║ ██╔══██╗ ████╗  ██║ ██╔═══██╗",
            " ██╔══██╗ ██╔════╝  ██╔════╝  ████╗  ██║  ╚══██╔══╝"
        ),
        (
            "██╔██╗ ██║ ███████║ ██╔██╗ ██║ ██║   ██║",
            " ███████║ ██║  ███╗ █████╗    ██╔██╗ ██║     ██║"
        ),
        (
            "██║╚██╗██║ ██╔══██║ ██║╚██╗██║ ██║   ██║",
            " ██╔══██║ ██║   ██║ ██╔══╝    ██║╚██╗██║     ██║"
        ),
        (
            "██║ ╚████║ ██║  ██║ ██║ ╚████║ ╚██████╔╝",
            " ██║  ██║ ╚██████╔╝ ███████╗  ██║ ╚████║     ██║"
        ),
        (
            "╚═╝  ╚═══╝ ╚═╝  ╚═╝ ╚═╝  ╚═══╝  ╚═════╝",
            "  ╚═╝  ╚═╝  ╚═════╝  ╚══════╝  ╚═╝  ╚═══╝     ╚═╝"
        )
    ];

    internal static string BuildHeaderBodyMarkup()
    {
        List<string> lines = [];

        lines.Add("[grey]  [/]");
        for (int index = 0; index < Wordmark.Length; index++)
        {
            string accentColor = index < 3 ? "fuchsia" : "purple";
            lines.Add(
                $"[grey]  [/][{accentColor}] [/][white]{Markup.Escape(Wordmark[index].Nano)}[/][fuchsia]{Markup.Escape(Wordmark[index].Agent)}[/]");
        }

        lines.Add("[grey]  [/]");

        return string.Join('\n', lines);
    }
}
