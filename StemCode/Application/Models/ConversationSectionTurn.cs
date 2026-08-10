using StemCode.Application.Utilities;
using System.Text.Json.Serialization;

namespace StemCode.Application.Models;

public sealed class ConversationSectionTurn
{
    [JsonConstructor]
    public ConversationSectionTurn()
    {
        TurnId = Guid.NewGuid().ToString("D");
        UserInput = string.Empty;
        ToolCalls = [];
        ToolOutputMessages = [];
        Attachments = [];
        Status = ConversationTurnStatus.Completed;
    }

    public ConversationSectionTurn(
        string userInput,
        string? assistantResponse = null,
        IReadOnlyList<ConversationToolCall>? toolCalls = null,
        IReadOnlyList<string>? toolOutputMessages = null,
        string? assistantReasoningContent = null,
        string? assistantReasoningDetailsJson = null,
        ConversationTurnStatus status = ConversationTurnStatus.Completed,
        string? turnId = null,
        IReadOnlyList<ConversationAttachment>? attachments = null,
        ConversationFailureInfo? failureInfo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);

        UserInput = SecretRedactor.Redact(userInput.Trim());
        Status = status == ConversationTurnStatus.Pending &&
            !string.IsNullOrWhiteSpace(assistantResponse) &&
            failureInfo is null
                ? ConversationTurnStatus.Completed
                : status;
        AssistantResponse = NormalizeOptionalText(assistantResponse);
        AssistantReasoningContent = NormalizeOptionalText(assistantReasoningContent);
        AssistantReasoningDetailsJson = NormalizeOptionalText(assistantReasoningDetailsJson);
        TurnId = string.IsNullOrWhiteSpace(turnId)
            ? Guid.NewGuid().ToString("D")
            : turnId.Trim();
        Attachments = (attachments ?? [])
            .Where(static attachment => attachment is not null)
            .Select(static attachment => new ConversationAttachment(
                attachment.Name,
                attachment.MediaType,
                attachment.ContentBase64,
                attachment.TextContent))
            .ToArray();
        FailureInfo = failureInfo;
        ToolCalls = (toolCalls ?? [])
            .Where(static toolCall =>
                toolCall is not null &&
                !string.IsNullOrWhiteSpace(toolCall.Id) &&
                !string.IsNullOrWhiteSpace(toolCall.Name) &&
                !string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
            .Select(static toolCall => new ConversationToolCall(
                toolCall.Id.Trim(),
                toolCall.Name.Trim(),
                SecretRedactor.Redact(toolCall.ArgumentsJson.Trim())))
            .ToArray();
        ToolOutputMessages = (toolOutputMessages ?? [])
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Select(static message => SecretRedactor.Redact(message.Trim()))
            .ToArray();

        if ((Status == ConversationTurnStatus.Completed) &&
            string.IsNullOrWhiteSpace(AssistantResponse))
        {
            throw new ArgumentException(
                "Completed turns must include an assistant response.",
                nameof(assistantResponse));
        }
    }

    public IReadOnlyList<ConversationAttachment> Attachments { get; init; }

    public ConversationFailureInfo? FailureInfo { get; init; }

    public string? AssistantReasoningContent { get; init; }

    public string? AssistantReasoningDetailsJson { get; init; }

    public string? AssistantResponse { get; init; }

    public ConversationTurnStatus Status { get; init; }

    public IReadOnlyList<ConversationToolCall> ToolCalls { get; init; }

    public IReadOnlyList<string> ToolOutputMessages { get; init; }

    public string TurnId { get; init; }

    public string UserInput { get; init; }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : SecretRedactor.Redact(value.Trim());
    }
}
