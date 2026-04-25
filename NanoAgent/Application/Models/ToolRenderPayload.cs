using NanoAgent.Application.Utilities;

namespace NanoAgent.Application.Models;

public sealed class ToolRenderPayload
{
    public ToolRenderPayload(string title, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Title = SecretRedactor.Redact(title.Trim());
        Text = SecretRedactor.Redact(text.Trim());
    }

    public string Text { get; }

    public string Title { get; }
}
