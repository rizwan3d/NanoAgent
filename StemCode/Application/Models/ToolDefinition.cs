using System.Text.Json;

namespace StemCode.Application.Models;

public sealed class ToolDefinition
{
    public ToolDefinition(
        string name,
        string description,
        JsonElement schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Tool schema must be a JSON object.",
                nameof(schema));
        }

        Name = name.Trim();
        Description = description.Trim();
        Schema = schema.Clone();
    }

    public string Description { get; }

    public string Name { get; }

    public JsonElement Schema { get; }
}
