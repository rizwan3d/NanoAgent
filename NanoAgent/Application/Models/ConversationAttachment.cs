namespace NanoAgent.Application.Models;

public sealed class ConversationAttachment
{
    public ConversationAttachment(
        string name,
        string mediaType,
        string contentBase64,
        string? textContent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentBase64);

        Name = name.Trim();
        MediaType = mediaType.Trim();
        ContentBase64 = contentBase64.Trim();
        TextContent = string.IsNullOrWhiteSpace(textContent)
            ? null
            : textContent;
    }

    public string ContentBase64 { get; }

    public bool IsImage => MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public bool IsText => !string.IsNullOrWhiteSpace(TextContent);

    public string MediaType { get; }

    public string Name { get; }

    public string? TextContent { get; }

    public string ToDataUri()
    {
        return $"data:{MediaType};base64,{ContentBase64}";
    }
}
