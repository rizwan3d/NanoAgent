namespace NanoAgent.ConsoleHost.Rendering;

internal enum CliOutputStyle
{
    Default = 0,
    AssistantLabel = 1,
    AssistantText = 2,
    Heading = 3,
    Strong = 4,
    Emphasis = 5,
    InlineCode = 6,
    CodeFence = 7,
    CodeText = 8,
    DiffAddition = 9,
    DiffRemoval = 10,
    DiffHeader = 11,
    DiffContext = 12,
    Warning = 13,
    Error = 14,
    Info = 15,
    Muted = 16,
    Link = 17
}
