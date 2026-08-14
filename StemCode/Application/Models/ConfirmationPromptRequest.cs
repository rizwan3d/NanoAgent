namespace StemCode.Application.Models;

public sealed record ConfirmationPromptRequest(
    string Title,
    string? Description = null,
    bool DefaultValue = true,
    bool AllowCancellation = true);
