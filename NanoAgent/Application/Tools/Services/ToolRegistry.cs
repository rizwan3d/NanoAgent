using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using System.Text.Json;

namespace NanoAgent.Application.Tools.Services;

internal sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<ToolDefinition> _toolDefinitions;
    private readonly IReadOnlyDictionary<string, ToolRegistration> _tools;

    public ToolRegistry(
        IEnumerable<ITool> tools,
        IPermissionParser permissionParser,
        IEnumerable<IDynamicToolProvider>? dynamicToolProviders = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(permissionParser);

        Dictionary<string, ToolRegistration> toolMap = new(StringComparer.Ordinal);
        List<ToolDefinition> definitions = [];

        foreach (ITool tool in tools.Concat(GetDynamicTools(dynamicToolProviders)))
        {
            if (string.IsNullOrWhiteSpace(tool.Description))
            {
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' must provide a description.");
            }

            ToolDefinition definition = new(
                tool.Name,
                tool.Description,
                ParseSchema(tool));

            ToolPermissionPolicy permissionPolicy = permissionParser.Parse(
                tool.Name,
                tool.PermissionRequirements);

            if (!toolMap.TryAdd(tool.Name, new ToolRegistration(tool, permissionPolicy)))
            {
                throw new InvalidOperationException(
                    $"Duplicate tool registration detected for '{tool.Name}'.");
            }

            definitions.Add(definition);
        }

        _tools = toolMap;
        _toolDefinitions = definitions
            .OrderBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return _toolDefinitions;
    }

    public IReadOnlyList<string> GetRegisteredToolNames()
    {
        return _toolDefinitions
            .Select(static definition => definition.Name)
            .ToArray();
    }

    public bool TryResolve(string toolName, out ToolRegistration? tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        return _tools.TryGetValue(toolName.Trim(), out tool);
    }

    private static JsonElement ParseSchema(ITool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Schema))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' must provide a JSON schema.");
        }

        using JsonDocument schemaDocument = JsonDocument.Parse(tool.Schema);
        if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' must provide a JSON-object schema.");
        }

        return schemaDocument.RootElement.Clone();
    }

    private static IEnumerable<ITool> GetDynamicTools(IEnumerable<IDynamicToolProvider>? dynamicToolProviders)
    {
        if (dynamicToolProviders is null)
        {
            return [];
        }

        return dynamicToolProviders.SelectMany(static provider => provider.GetTools());
    }
}
