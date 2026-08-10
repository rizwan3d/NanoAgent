using StemCode.Application.Models;

namespace StemCode.Application.Abstractions;

public interface IToolInvoker
{
    Task<ToolInvocationResult> InvokeAsync(
        ConversationToolCall toolCall,
        ReplSessionContext session,
        ConversationExecutionPhase executionPhase,
        IReadOnlySet<string> allowedToolNames,
        CancellationToken cancellationToken);
}
