using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Application.Commands;

internal static class SessionCommandSupport
{
    public const int DefaultCompactRetainedTurns = 4;

    public static ConversationSectionSnapshot CreateSnapshot(
        ReplSessionContext session,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.CreateSectionSnapshot(updatedAtUtc);
    }

    public static ConversationSectionSnapshot CreateCopySnapshot(
        ReplSessionContext source,
        string title,
        IReadOnlyList<ConversationSectionTurn> turns,
        int totalEstimatedOutputTokens,
        bool includeState)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(turns);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ConversationSectionSnapshot(
            Guid.NewGuid().ToString("D"),
            title,
            now,
            now,
            source.ProviderProfile,
            source.ActiveModelId,
            source.AvailableModelIds,
            turns,
            Math.Max(0, totalEstimatedOutputTokens),
            includeState ? source.PendingExecutionPlan : null,
            source.AgentProfile.Name,
            source.ReasoningEffort,
            includeState ? source.SessionState : SessionStateSnapshot.Empty,
            source.WorkspacePath,
            source.ModelContextWindowTokens);
    }

    public static async Task<ReplSessionContext> SaveAndResumeAsync(
        ConversationSectionSnapshot snapshot,
        IConversationSectionStore sectionStore,
        ISessionAppService sessionAppService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sectionStore);
        ArgumentNullException.ThrowIfNull(sessionAppService);

        await sectionStore.SaveAsync(snapshot, cancellationToken);
        return await sessionAppService.ResumeAsync(
            new ResumeSessionRequest(snapshot.SectionId),
            cancellationToken);
    }

    public static string CreateDefaultExportPath(
        ReplSessionContext session,
        string extension)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        string normalizedExtension = extension.Trim().TrimStart('.');
        string title = SanitizeFileName(session.SectionTitle);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "session";
        }

        string fileName = $"nanoagent-{title}-{session.SectionId[..8]}.{normalizedExtension}";
        return Path.Combine(Directory.GetCurrentDirectory(), fileName);
    }

    public static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string expanded = path.Trim().Trim('"');
        if (expanded.StartsWith("~/", StringComparison.Ordinal) ||
            expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = Path.Combine(home, expanded[2..]);
        }

        return Path.GetFullPath(expanded);
    }

    public static async Task ExportJsonAsync(
        ConversationSectionSnapshot snapshot,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        EnsureParentDirectory(filePath);
        await using FileStream stream = new(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await JsonSerializer.SerializeAsync(
            stream,
            snapshot,
            ConversationSectionStorageJsonContext.Default.ConversationSectionSnapshot,
            cancellationToken);
    }

    public static async Task<ConversationSectionSnapshot?> LoadJsonAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        try
        {
            return await JsonSerializer.DeserializeAsync(
                stream,
                ConversationSectionStorageJsonContext.Default.ConversationSectionSnapshot,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static async Task ExportHtmlAsync(
        ConversationSectionSnapshot snapshot,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        EnsureParentDirectory(filePath);
        string html = CreateHtmlTranscript(snapshot);
        await File.WriteAllTextAsync(filePath, html, Encoding.UTF8, cancellationToken);
    }

    public static ConversationSectionSnapshot CreateImportedSnapshot(
        ConversationSectionSnapshot imported,
        ReplSessionContext currentSession)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(currentSession);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string title = imported.Title.EndsWith(" imported", StringComparison.OrdinalIgnoreCase)
            ? imported.Title
            : imported.Title + " imported";

        return new ConversationSectionSnapshot(
            Guid.NewGuid().ToString("D"),
            title,
            now,
            now,
            imported.ProviderProfile,
            imported.ActiveModelId,
            imported.AvailableModelIds,
            imported.Turns,
            imported.TotalEstimatedOutputTokens,
            imported.PendingExecutionPlan,
            imported.AgentProfileName,
            imported.ReasoningEffort,
            imported.SessionState,
            currentSession.WorkspacePath,
            imported.ModelContextWindowTokens);
    }

    public static bool TryNormalizeSessionId(string value, out string sessionId)
    {
        sessionId = string.Empty;
        if (!Guid.TryParse(value.Trim(), out Guid parsed))
        {
            return false;
        }

        sessionId = parsed.ToString("D");
        return true;
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("u", CultureInfo.InvariantCulture);
    }

    public static string CreateTitleWithSuffix(string title, string suffix)
    {
        string normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? ReplSessionContext.DefaultSectionTitle
            : title.Trim();

        return normalizedTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normalizedTitle
            : normalizedTitle + " " + suffix;
    }

    public static string CreatePreview(string value, int maxLength = 72)
    {
        string normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private static string CreateHtmlTranscript(ConversationSectionSnapshot snapshot)
    {
        StringBuilder builder = new();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.Append("<title>");
        builder.Append(Html(snapshot.Title));
        builder.AppendLine("</title>");
        builder.AppendLine("<style>");
        builder.AppendLine(":root{color-scheme:light dark;font-family:Inter,Segoe UI,Arial,sans-serif;background:#f7f7f5;color:#20201d}");
        builder.AppendLine("body{margin:0;padding:40px;line-height:1.55}");
        builder.AppendLine("main{max-width:960px;margin:0 auto}");
        builder.AppendLine("header{border-bottom:1px solid #d9d7d0;margin-bottom:28px;padding-bottom:20px}");
        builder.AppendLine("h1{font-size:28px;margin:0 0 10px}");
        builder.AppendLine(".meta{display:grid;grid-template-columns:160px 1fr;gap:6px 16px;color:#54524b;font-size:14px}");
        builder.AppendLine(".turn{border-top:1px solid #ddd9d0;padding:24px 0}");
        builder.AppendLine(".role{font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:0;color:#6a675e;margin:0 0 8px}");
        builder.AppendLine("pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#fff;border:1px solid #dedbd3;border-radius:8px;padding:14px;margin:0}");
        builder.AppendLine(".tool{margin:10px 0;color:#54524b;font-size:14px}");
        builder.AppendLine("@media (prefers-color-scheme:dark){:root{background:#171717;color:#eeece6}.meta,.role,.tool{color:#b8b3a7}header,.turn{border-color:#3a3935}pre{background:#20201e;border-color:#3c3a35}}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main>");
        builder.AppendLine("<header>");
        builder.Append("<h1>");
        builder.Append(Html(snapshot.Title));
        builder.AppendLine("</h1>");
        builder.AppendLine("<div class=\"meta\">");
        AppendMeta(builder, "Session", snapshot.SectionId);
        AppendMeta(builder, "Created", FormatTimestamp(snapshot.CreatedAtUtc));
        AppendMeta(builder, "Updated", FormatTimestamp(snapshot.UpdatedAtUtc));
        AppendMeta(builder, "Provider", snapshot.ProviderProfile.ProviderKind.ToDisplayName());
        AppendMeta(builder, "Model", snapshot.ActiveModelId);
        AppendMeta(builder, "Profile", snapshot.AgentProfileName);
        AppendMeta(builder, "Turns", snapshot.Turns.Count.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("</div>");
        builder.AppendLine("</header>");

        for (int index = 0; index < snapshot.Turns.Count; index++)
        {
            ConversationSectionTurn turn = snapshot.Turns[index];
            builder.AppendLine("<section class=\"turn\">");
            builder.Append("<p class=\"role\">User turn ");
            builder.Append((index + 1).ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("</p>");
            AppendPre(builder, turn.UserInput);

            if (turn.ToolOutputMessages.Count > 0 || turn.ToolCalls.Count > 0)
            {
                builder.AppendLine("<div class=\"tool\">");
                foreach (string output in turn.ToolOutputMessages)
                {
                    builder.Append("<p>");
                    builder.Append(Html(CreatePreview(output, 180)));
                    builder.AppendLine("</p>");
                }

                foreach (ConversationToolCall call in turn.ToolCalls)
                {
                    builder.Append("<p>Tool: ");
                    builder.Append(Html(call.Name));
                    builder.AppendLine("</p>");
                }

                builder.AppendLine("</div>");
            }

            builder.AppendLine("<p class=\"role\">Assistant</p>");
            AppendPre(builder, turn.AssistantResponse);
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendMeta(StringBuilder builder, string label, string value)
    {
        builder.Append("<div>");
        builder.Append(Html(label));
        builder.AppendLine("</div>");
        builder.Append("<div>");
        builder.Append(Html(value));
        builder.AppendLine("</div>");
    }

    private static void AppendPre(StringBuilder builder, string value)
    {
        builder.Append("<pre>");
        builder.Append(Html(value));
        builder.AppendLine("</pre>");
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static void EnsureParentDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        HashSet<char> invalid = new(Path.GetInvalidFileNameChars());
        StringBuilder builder = new();
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (invalid.Contains(character))
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        string sanitized = builder.ToString().Trim('-');
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized.Length <= 48
            ? sanitized
            : sanitized[..48].Trim('-');
    }
}
