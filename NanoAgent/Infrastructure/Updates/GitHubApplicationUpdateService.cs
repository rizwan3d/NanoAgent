using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Secrets;

namespace NanoAgent.Infrastructure.Updates;

internal sealed class GitHubApplicationUpdateService : IApplicationUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/rizwan3d/NanoAgent/releases/latest";
    private const string ReleasePageUrl = "https://github.com/rizwan3d/NanoAgent/releases/latest";
    private const string InstallScriptUrl = "https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.sh";
    private const string InstallPowerShellScriptUrl = "https://raw.githubusercontent.com/rizwan3d/NanoAgent/master/scripts/install.ps1";

    private readonly HttpClient _httpClient;
    private readonly IProcessRunner _processRunner;

    public GitHubApplicationUpdateService(
        HttpClient httpClient,
        IProcessRunner processRunner)
    {
        _httpClient = httpClient;
        _processRunner = processRunner;
    }

    public async Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            LatestReleaseApiUrl,
            cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Unable to check for updates. GitHub returned HTTP {(int)response.StatusCode}: {Truncate(responseBody.Trim(), 200)}");
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;
        string latestVersion = TryGetString(root, "tag_name")
            ?? throw new InvalidOperationException("Unable to check for updates. GitHub did not return a release tag.");
        string releaseUrl = TryGetString(root, "html_url") ?? ReleasePageUrl;
        string currentVersion = GetCurrentVersion();

        return new ApplicationUpdateInfo(
            currentVersion,
            latestVersion,
            new Uri(releaseUrl),
            IsUpdateAvailable(currentVersion, latestVersion));
    }

    public async Task<ApplicationUpdateInstallResult> InstallAsync(
        ApplicationUpdateInfo updateInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);

        if (!updateInfo.IsUpdateAvailable)
        {
            return new ApplicationUpdateInstallResult(
                true,
                $"NanoAgent is already up to date ({updateInfo.CurrentVersion}).");
        }

        ProcessExecutionRequest request = CreateInstallRequest(updateInfo.LatestVersion);
        ProcessExecutionResult result = await _processRunner.RunAsync(request, cancellationToken);

        if (result.ExitCode == 0)
        {
            string successMessage = OperatingSystem.IsWindows()
                ? $"NanoAgent update prepared: {updateInfo.LatestVersion}. Exit NanoAgent to finish installation, then restart it to use the new version."
                : $"NanoAgent update installed: {updateInfo.LatestVersion}. Restart NanoAgent to use the new version.";

            return new ApplicationUpdateInstallResult(
                true,
                successMessage);
        }

        string detail = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Select(static text => text.Trim()));

        return new ApplicationUpdateInstallResult(
            false,
            string.IsNullOrWhiteSpace(detail)
                ? $"NanoAgent update failed with exit code {result.ExitCode}. Download it manually from {updateInfo.ReleaseUri}."
                : $"NanoAgent update failed with exit code {result.ExitCode}: {Truncate(detail, 600)}");
    }

    private static ProcessExecutionRequest CreateInstallRequest(string latestVersion)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["NanoAgent_TAG"] = latestVersion
        };

        if (OperatingSystem.IsWindows())
        {
            environment["NanoAgent_WAIT_FOR_PROCESS_ID"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

            return new ProcessExecutionRequest(
                "powershell.exe",
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    $"irm {InstallPowerShellScriptUrl} | iex"
                ],
                MaxOutputCharacters: 20_000,
                EnvironmentVariables: environment);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new ProcessExecutionRequest(
                "sh",
                [
                    "-c",
                    $"curl -fsSL {InstallScriptUrl} | bash"
                ],
                MaxOutputCharacters: 20_000,
                EnvironmentVariables: environment);
        }

        throw new PlatformNotSupportedException(
            "Automatic updates are supported on Windows, Linux, and macOS.");
    }

    private static string GetCurrentVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubApplicationUpdateService).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return NormalizeVersionText(
            informationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "0.0.0");
    }

    private static bool IsUpdateAvailable(string currentVersion, string latestVersion)
    {
        string normalizedCurrent = NormalizeVersionText(currentVersion);
        string normalizedLatest = NormalizeVersionText(latestVersion);

        if (TryParseVersion(normalizedCurrent, out Version? current) &&
            TryParseVersion(normalizedLatest, out Version? latest))
        {
            return latest > current;
        }

        return !string.Equals(normalizedLatest, normalizedCurrent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        string normalized = value;
        int dashIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            normalized = normalized[..dashIndex];
        }

        return Version.TryParse(normalized, out version);
    }

    private static string NormalizeVersionText(string value)
    {
        string normalized = value.Trim();
        int metadataIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        return normalized.StartsWith('v') || normalized.StartsWith('V')
            ? normalized[1..]
            : normalized;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = property.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }
}
