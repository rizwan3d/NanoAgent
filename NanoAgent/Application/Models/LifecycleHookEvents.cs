namespace NanoAgent.Application.Models;

public static class LifecycleHookEvents
{
    public const string BeforeTaskStart = "before_task_start";
    public const string AfterTaskComplete = "after_task_complete";
    public const string AfterTaskFailed = "after_task_failed";
    public const string BeforeToolCall = "before_tool_call";
    public const string AfterToolCall = "after_tool_call";
    public const string AfterToolFailure = "after_tool_failure";
    public const string OnPermissionDenied = "on_permission_denied";
    public const string BeforeFileRead = "before_file_read";
    public const string AfterFileRead = "after_file_read";
    public const string BeforeFileWrite = "before_file_write";
    public const string AfterFileWrite = "after_file_write";
    public const string BeforeFileDelete = "before_file_delete";
    public const string AfterFileDelete = "after_file_delete";
    public const string BeforeFileSearch = "before_file_search";
    public const string AfterFileSearch = "after_file_search";
    public const string BeforeShellCommand = "before_shell_command";
    public const string AfterShellCommand = "after_shell_command";
    public const string AfterShellFailure = "after_shell_failure";
    public const string BeforeWebRequest = "before_web_request";
    public const string AfterWebRequest = "after_web_request";
    public const string BeforeMemorySave = "before_memory_save";
    public const string AfterMemorySave = "after_memory_save";
    public const string BeforeMemoryWrite = "before_memory_write";
    public const string AfterMemoryWrite = "after_memory_write";
    public const string BeforeAgentDelegate = "before_agent_delegate";
    public const string AfterAgentDelegate = "after_agent_delegate";

    public static bool IsBeforeEvent(string eventName)
    {
        return Normalize(eventName).StartsWith("before_", StringComparison.Ordinal);
    }

    public static string Normalize(string eventName)
    {
        return string.IsNullOrWhiteSpace(eventName)
            ? string.Empty
            : eventName.Trim().Replace('-', '_').ToLowerInvariant();
    }
}
