namespace NanoAgent.Application.Formatting;

/// <summary>
/// Controls how tool results are rendered in session output.
/// </summary>
public static class ToolOutputDisplay
{
    /// <summary>
    /// Explicit user override set with the <c>/tooloutput</c> command.
    /// <see langword="null"/> means "follow the active agent profile preference".
    /// </summary>
    public static bool? FullToolOutputOverride { get; set; }

    /// <summary>
    /// Tool output preference contributed by the active agent profile.
    /// <see langword="null"/> means the profile expresses no preference.
    /// </summary>
    public static bool? ProfileFullToolOutput { get; set; }

    /// <summary>
    /// Tool output default contributed by <c>agent-profile.json</c>
    /// (<c>Application.Tools.toolOutput</c>). Applies when neither the command
    /// override nor the active profile expresses a preference.
    /// <see langword="null"/> means no configured default.
    /// </summary>
    public static bool? ConfiguredDefaultFullToolOutput { get; set; }

    /// <summary>
    /// Gets whether tool results should render the complete output instead of the
    /// compact preview. The command override wins; then the active profile
    /// preference; then the configured default; otherwise the compact preview.
    /// </summary>
    public static bool ShowFullToolOutput =>
        FullToolOutputOverride
            ?? ProfileFullToolOutput
            ?? ConfiguredDefaultFullToolOutput
            ?? false;

    /// <summary>
    /// Parses a tool output preference value. Returns <see langword="true"/> for
    /// full/complete, <see langword="false"/> for compact/preview, and
    /// <see langword="null"/> for empty or unrecognized values.
    /// </summary>
    public static bool? ParsePreference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "full" or "complete" or "all" or "on" or "true" => true,
            "compact" or "preview" or "short" or "off" or "false" => false,
            _ => null
        };
    }
}
