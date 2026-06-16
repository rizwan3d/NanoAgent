namespace NanoAgent.Application.Tools.Models;

public sealed record AskQuestionResult(
    string Question,
    string? Header,
    bool MultiSelect,
    bool Answered,
    IReadOnlyList<string> Answers);
