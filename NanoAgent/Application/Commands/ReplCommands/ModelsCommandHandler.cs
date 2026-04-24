using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Commands;

namespace NanoAgent.Application.Commands;

internal sealed class ModelsCommandHandler : IReplCommandHandler
{
    public string CommandName => "models";

    public string Description => "List the available models and highlight the active one.";

    public string Usage => "/models";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        string[] lines =
        [
            $"Available models ({context.Session.AvailableModelIds.Count}):",
            .. context.Session.AvailableModelIds.Select(modelId =>
                modelId == context.Session.ActiveModelId
                    ? $"* {modelId} (active)"
                    : $"* {modelId}")
        ];

        return Task.FromResult(ReplCommandResult.Continue(string.Join(Environment.NewLine, lines)));
    }
}
