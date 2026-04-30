using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Tools.Serialization;
using System.Text.Json;

namespace NanoAgent.Infrastructure.Mcp;

internal sealed class McpTool : ITool
{
    private readonly IMcpServerClient _client;
    private readonly string _permissionRequirements;
    private readonly string _remoteToolName;
    private readonly TimeSpan _timeout;

    public McpTool(
        string name,
        string remoteToolName,
        string description,
        JsonElement schema,
        IMcpServerClient client,
        ToolApprovalMode approvalMode,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteToolName);
        ArgumentNullException.ThrowIfNull(client);

        Name = name.Trim();
        _remoteToolName = remoteToolName.Trim();
        Description = string.IsNullOrWhiteSpace(description)
            ? $"Call MCP tool '{_remoteToolName}' from server '{client.ServerName}'."
            : description.Trim();
        Schema = schema.ValueKind == JsonValueKind.Object
            ? schema.GetRawText()
            : McpJson.CreateDefaultSchema().GetRawText();
        _client = client;
        _timeout = timeout;
        _permissionRequirements = McpJson.CreatePermissionRequirements(
            client.ServerName,
            _remoteToolName,
            approvalMode);
    }

    public string Description { get; }

    public string Name { get; }

    public string PermissionRequirements => _permissionRequirements;

    public string Schema { get; }

    public async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_timeout);
            McpCallToolResult result = await _client.CallToolAsync(
                _remoteToolName,
                context.Arguments,
                timeoutSource.Token);
            JsonElement payload = McpJson.CreateToolResultPayload(
                _client.ServerName,
                _remoteToolName,
                result.IsError,
                result.Result);
            ToolRenderPayload renderPayload = new(
                $"MCP: {_client.ServerName}/{_remoteToolName}",
                string.IsNullOrWhiteSpace(result.RenderText)
                    ? result.Result.GetRawText()
                    : result.RenderText);

            if (result.IsError)
            {
                return ToolResult.ExecutionError(
                    $"MCP tool '{_remoteToolName}' on server '{_client.ServerName}' returned an error.",
                    payload.GetRawText(),
                    renderPayload);
            }

            return ToolResultFactory.Success(
                $"MCP tool '{_remoteToolName}' on server '{_client.ServerName}' completed.",
                payload,
                ToolJsonContext.Default.JsonElement,
                renderPayload);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResultFactory.ExecutionError(
                "mcp_tool_timeout",
                $"MCP tool '{_remoteToolName}' on server '{_client.ServerName}' timed out after {_timeout.TotalSeconds:0} seconds.",
                new ToolRenderPayload(
                    $"MCP timed out: {_client.ServerName}/{_remoteToolName}",
                    $"The MCP tool did not finish within {_timeout.TotalSeconds:0} seconds."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ToolResultFactory.ExecutionError(
                "mcp_tool_failed",
                $"MCP tool '{_remoteToolName}' on server '{_client.ServerName}' failed: {exception.Message}",
                new ToolRenderPayload(
                    $"MCP failed: {_client.ServerName}/{_remoteToolName}",
                    exception.Message));
        }
    }
}
