using System.Text.Json.Serialization;

namespace NanoAgent;

internal sealed class FileToolService : IToolService
{
    private readonly Dictionary<string, IToolHandler> _handlers;

    public FileToolService()
        : this(CreateDefaultHandlers())
    {
    }

    public FileToolService(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.Name, StringComparer.Ordinal);
    }

    public ChatToolDefinition[] GetToolDefinitions() =>
        _handlers.Values.Select(handler => handler.Definition).ToArray();

    public string Execute(ChatToolCall toolCall) =>
        _handlers.TryGetValue(toolCall.Function.Name, out IToolHandler? handler)
            ? handler.Execute(toolCall)
            : $"Tool error: unsupported tool '{toolCall.Function.Name}'.";

    private static IToolHandler[] CreateDefaultHandlers() =>
    [
        new ReadFileToolHandler(),
        new ListFilesToolHandler(),
        new WriteFileToolHandler(),
        new EditFileToolHandler(),
        new ApplyPatchToolHandler(),
        new CodeSearchToolHandler(),
        new RunCommandToolHandler()
    ];
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ReadFileToolArguments))]
[JsonSerializable(typeof(ListFilesToolArguments))]
[JsonSerializable(typeof(RunCommandToolArguments))]
[JsonSerializable(typeof(WriteFileToolArguments))]
[JsonSerializable(typeof(EditFileToolArguments))]
[JsonSerializable(typeof(CodeSearchToolArguments))]
[JsonSerializable(typeof(ApplyPatchToolArguments))]
internal sealed partial class FileToolJsonContext : JsonSerializerContext
{
}
