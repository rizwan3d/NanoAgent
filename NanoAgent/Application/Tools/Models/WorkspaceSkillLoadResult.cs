namespace NanoAgent.Application.Tools.Models;

public sealed record WorkspaceSkillLoadResult(
    string Name,
    string Description,
    string Path,
    string Instructions,
    int CharacterCount,
    bool WasTruncated);
