namespace NanoAgent;

internal readonly record struct FilePreviewLine(int? Number, string Text);

internal interface IChatConsole
{
    void RenderHeader(AppConfig config, string sessionId, bool isResumedSession);
    string? ReadUserInput();
    void RenderUserMessage(string userInput);
    void RenderCommandMessage(string command);
    void RenderMutedToolCall(string toolName);
    void RenderFileOperationMessage(
        string operation,
        string path,
        string summary,
        IReadOnlyList<FilePreviewLine> previewLines,
        int hiddenLineCount);
    void BeginAgentActivity();
    void UpdateAgentActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate);
    void CompleteAgentActivity(TimeSpan elapsed, int? outputTokens, bool isEstimate);
    void RenderAgentMessage(string message);
    void RenderVerboseMessage(string message);
}
