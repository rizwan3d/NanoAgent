using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Commands;

namespace NanoAgent.Application.Commands;

internal sealed class ModelsCommandHandler : IReplCommandHandler
{
    private readonly IInteractiveModelSelectionService _modelSelectionService;

    public ModelsCommandHandler(IInteractiveModelSelectionService modelSelectionService)
    {
        _modelSelectionService = modelSelectionService;
    }

    public string CommandName => "models";

    public string Description => "Open the active model picker.";

    public string Usage => "/models";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return _modelSelectionService.SelectAsync(
            context.Session,
            cancellationToken);
    }
}
