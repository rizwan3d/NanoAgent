using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;

namespace NanoAgent.Application.Commands;

internal sealed class McpCommandHandler : IReplCommandHandler
{
    private readonly IEnumerable<IDynamicToolProvider> _dynamicToolProviders;
    private readonly IToolRegistry _toolRegistry;

    public McpCommandHandler(
        IEnumerable<IDynamicToolProvider> dynamicToolProviders,
        IToolRegistry toolRegistry)
    {
        _dynamicToolProviders = dynamicToolProviders;
        _toolRegistry = toolRegistry;
    }

    public string CommandName => "mcp";

    public string Description => "Show configured MCP servers and discovered MCP tools.";

    public string Usage => "/mcp";

    public Task<ReplCommandResult> ExecuteAsync(
        ReplCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DynamicToolProviderStatus[] statuses = _dynamicToolProviders
            .SelectMany(static provider => provider.GetStatuses())
            .OrderBy(static status => status.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] toolNames = _toolRegistry.GetToolDefinitions()
            .Select(static definition => definition.Name)
            .Where(static name => name.StartsWith("mcp__", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        List<string> lines = ["MCP servers:"];
        if (statuses.Length == 0)
        {
            lines.Add("No MCP servers are configured.");
        }
        else
        {
            foreach (DynamicToolProviderStatus status in statuses)
            {
                string state = status.Enabled
                    ? status.IsAvailable ? "available" : "unavailable"
                    : "disabled";
                string details = string.IsNullOrWhiteSpace(status.Details)
                    ? string.Empty
                    : $" - {status.Details}";

                lines.Add(
                    $"{status.Name} ({status.Kind}): {state}, {status.ToolCount} tool(s){details}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("MCP tools:");
        lines.AddRange(toolNames.Length == 0
            ? ["No MCP tools are currently available."]
            : toolNames);

        return Task.FromResult(ReplCommandResult.Continue(string.Join(Environment.NewLine, lines)));
    }
}
