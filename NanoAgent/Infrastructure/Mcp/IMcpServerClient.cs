using System.Text.Json;

namespace NanoAgent.Infrastructure.Mcp;

internal interface IMcpServerClient : IAsyncDisposable
{
    string Endpoint { get; }

    string ServerName { get; }

    string TransportKind { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<McpRemoteTool>> ListToolsAsync(CancellationToken cancellationToken);

    Task<McpCallToolResult> CallToolAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken);
}

internal sealed record McpRemoteTool(
    string Name,
    string Description,
    JsonElement InputSchema);

internal sealed record McpCallToolResult(
    bool IsError,
    JsonElement Result,
    string RenderText);
