using NanoAgent.Application.Abstractions;
using NanoAgent.Infrastructure.Storage;
using System.Collections.Concurrent;
using System.Text.Json;

namespace NanoAgent.Infrastructure.CodeIntelligence;

internal interface ILanguageServerRegistry
{
    Task<IReadOnlyList<LanguageServerStatusEntry>> GetStatusAsync(
        string workspaceRoot,
        bool refresh,
        CancellationToken cancellationToken);

    Task<LanguageServerResolution> ResolveAsync(
        string workspaceRoot,
        string fullPath,
        bool refresh,
        CancellationToken cancellationToken);

    void RecordServerHealth(
        string workspaceRoot,
        string serverKey,
        LanguageServerHealthState state,
        string? message = null);
}

internal sealed class LanguageServerRegistry : ILanguageServerRegistry
{
    private readonly IUserDataPathProvider _userDataPathProvider;
    private readonly IWorkspaceRootProvider _workspaceRootProvider;
    private readonly ConcurrentDictionary<string, LanguageServerProbeResult> _probeCache = new(StringComparer.OrdinalIgnoreCase);

    public LanguageServerRegistry(
        IUserDataPathProvider userDataPathProvider,
        IWorkspaceRootProvider workspaceRootProvider)
    {
        _userDataPathProvider = userDataPathProvider;
        _workspaceRootProvider = workspaceRootProvider;
    }

    public Task<LanguageServerResolution> ResolveAsync(
        string workspaceRoot,
        string fullPath,
        bool refresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string extension = NormalizeExtension(Path.GetExtension(fullPath));
        if (refresh)
        {
            ClearWorkspaceCache(workspaceRoot);
        }

        List<LanguageServerDescriptor> servers = BuildDescriptors()
            .Where(server => server.Enabled && server.FileExtensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(static server => server.Priority)
            .ThenBy(static server => server.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<ResolvedLanguageServer> candidates = [];
        foreach (LanguageServerDescriptor server in servers)
        {
            LanguageServerProbeResult probe = Probe(workspaceRoot, server);
            candidates.Add(new ResolvedLanguageServer(server, probe));
        }

        string languageId = candidates
            .Select(candidate => candidate.Descriptor.GetLanguageId(extension))
            .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id))
            ?? extension.TrimStart('.');

        return Task.FromResult(new LanguageServerResolution(extension, languageId, candidates));
    }

    public Task<IReadOnlyList<LanguageServerStatusEntry>> GetStatusAsync(
        string workspaceRoot,
        bool refresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (refresh)
        {
            ClearWorkspaceCache(workspaceRoot);
        }

        LanguageServerStatusEntry[] entries = BuildDescriptors()
            .GroupBy(static descriptor => descriptor.Language, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase);
                List<LanguageServerCandidateStatus> candidates = [];
                foreach (LanguageServerDescriptor descriptor in group
                             .OrderByDescending(static item => item.Priority)
                             .ThenBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (string extension in descriptor.FileExtensions)
                    {
                        extensions.Add(extension);
                    }

                    LanguageServerProbeResult probe = Probe(workspaceRoot, descriptor);
                    candidates.Add(new LanguageServerCandidateStatus(
                        descriptor.Key,
                        descriptor.Name,
                        descriptor.Command,
                        descriptor.Arguments,
                        descriptor.Priority,
                        probe.State.ToString().ToLowerInvariant(),
                        descriptor.Source,
                        probe.ResolvedCommand,
                        descriptor.InstallHint,
                        descriptor.LanguageId,
                        probe.Message));
                }

                string? selectedServer = candidates
                    .FirstOrDefault(static candidate => candidate.DetectionStatus is "healthy" or "detected")
                    ?.Name;

                return new LanguageServerStatusEntry(
                    group.Key,
                    group.First().LanguageId,
                    extensions.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
                    candidates,
                    selectedServer);
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<LanguageServerStatusEntry>>(entries);
    }

    public void RecordServerHealth(
        string workspaceRoot,
        string serverKey,
        LanguageServerHealthState state,
        string? message = null)
    {
        string cacheKey = CreateCacheKey(workspaceRoot, serverKey);
        if (!_probeCache.TryGetValue(cacheKey, out LanguageServerProbeResult? existing))
        {
            return;
        }

        _probeCache[cacheKey] = existing with
        {
            State = state,
            Message = string.IsNullOrWhiteSpace(message) ? existing.Message : message
        };
    }

    private List<LanguageServerDescriptor> BuildDescriptors()
    {
        Dictionary<string, LanguageServerDescriptor> descriptors = CreateBuiltIns()
            .ToDictionary(static item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (LanguageServerProfileConfiguration configuration in AgentProfileConfigurationReader.LoadLanguageServers(
                     _userDataPathProvider,
                     _workspaceRootProvider))
        {
            if (descriptors.TryGetValue(configuration.Key, out LanguageServerDescriptor? existing))
            {
                descriptors[configuration.Key] = existing.Apply(configuration);
                continue;
            }

            descriptors[configuration.Key] = LanguageServerDescriptor.FromConfiguration(configuration);
        }

        return descriptors.Values
            .Where(static descriptor =>
                !string.IsNullOrWhiteSpace(descriptor.Language) &&
                !string.IsNullOrWhiteSpace(descriptor.Command) &&
                descriptor.FileExtensions.Count > 0)
            .ToList();
    }

    private LanguageServerProbeResult Probe(
        string workspaceRoot,
        LanguageServerDescriptor descriptor)
    {
        string cacheKey = CreateCacheKey(workspaceRoot, descriptor.Key);
        if (_probeCache.TryGetValue(cacheKey, out LanguageServerProbeResult? cached))
        {
            return cached;
        }

        LanguageServerProbeResult probe = descriptor.Enabled
            ? ResolveCommand(workspaceRoot, descriptor)
            : new LanguageServerProbeResult(LanguageServerHealthState.Disabled, null, "Server is disabled.");
        _probeCache[cacheKey] = probe;
        return probe;
    }

    private static LanguageServerProbeResult ResolveCommand(
        string workspaceRoot,
        LanguageServerDescriptor descriptor)
    {
        foreach (string candidate in EnumerateCommandCandidates(workspaceRoot, descriptor.Command))
        {
            if (File.Exists(candidate))
            {
                return new LanguageServerProbeResult(LanguageServerHealthState.Detected, candidate, null);
            }
        }

        return new LanguageServerProbeResult(
            LanguageServerHealthState.Missing,
            null,
            $"Command '{descriptor.Command}' was not found.");
    }

    private static IEnumerable<string> EnumerateCommandCandidates(
        string workspaceRoot,
        string command)
    {
        if (Path.IsPathRooted(command) || command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
        {
            yield return Path.GetFullPath(command);
            yield break;
        }

        foreach (string directory in GetWorkspaceSearchDirectories(workspaceRoot))
        {
            foreach (string candidatePath in ExpandExecutableCandidates(directory, command))
            {
                yield return candidatePath;
            }
        }

        string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (string candidate in ExpandExecutableCandidates(directory, command))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> GetWorkspaceSearchDirectories(string workspaceRoot)
    {
        yield return Path.Combine(workspaceRoot, "node_modules", ".bin");
        yield return Path.Combine(workspaceRoot, ".venv", "Scripts");
        yield return Path.Combine(workspaceRoot, ".venv", "bin");
        yield return Path.Combine(workspaceRoot, "venv", "Scripts");
        yield return Path.Combine(workspaceRoot, "venv", "bin");
    }

    private static IEnumerable<string> ExpandExecutableCandidates(string directory, string command)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        yield return Path.Combine(directory, command);

        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        string extension = Path.GetExtension(command);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            yield break;
        }

        string pathExt = Environment.GetEnvironmentVariable("PATHEXT")
            ?? ".EXE;.CMD;.BAT;.COM";
        foreach (string item in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, command + item.ToLowerInvariant());
            yield return Path.Combine(directory, command + item.ToUpperInvariant());
        }
    }

    private void ClearWorkspaceCache(string workspaceRoot)
    {
        string prefix = Path.GetFullPath(workspaceRoot) + "|";
        foreach (string key in _probeCache.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            _probeCache.TryRemove(key, out _);
        }
    }

    private static string CreateCacheKey(string workspaceRoot, string serverKey)
    {
        return $"{Path.GetFullPath(workspaceRoot)}|{serverKey}";
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
    }

    private static IReadOnlyList<LanguageServerDescriptor> CreateBuiltIns()
    {
        return
        [
            new LanguageServerDescriptor("ts-vtsls", "TypeScript/JavaScript", "VTSLS", "vtsls", ["--stdio"], ["typescript", "javascript"], CreateLanguageIds(".ts", "typescript", ".tsx", "typescriptreact", ".mts", "typescript", ".cts", "typescript", ".js", "javascript", ".jsx", "javascriptreact", ".mjs", "javascript", ".cjs", "javascript"), [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"], 200, "npm install -g @vtsls/language-server typescript", null, true, "built-in"),
            new LanguageServerDescriptor("ts-typescript-language-server", "TypeScript/JavaScript", "TypeScript language server", "typescript-language-server", ["--stdio"], ["typescript", "javascript"], CreateLanguageIds(".ts", "typescript", ".tsx", "typescriptreact", ".mts", "typescript", ".cts", "typescript", ".js", "javascript", ".jsx", "javascriptreact", ".mjs", "javascript", ".cjs", "javascript"), [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"], 150, "npm install -g typescript-language-server typescript", null, true, "built-in"),
            new LanguageServerDescriptor("csharp-csharp-ls", "C#", "C# language server", "csharp-ls", [], ["csharp"], CreateLanguageIds(".cs", "csharp"), [".cs"], 200, "dotnet tool install --global csharp-ls", null, true, "built-in"),
            new LanguageServerDescriptor("csharp-roslyn", "C#", "Roslyn LSP", "Microsoft.CodeAnalysis.LanguageServer", [], ["csharp"], CreateLanguageIds(".cs", "csharp"), [".cs"], 150, "Install Roslyn language server and ensure its launcher is on PATH.", null, true, "built-in"),
            new LanguageServerDescriptor("python-basedpyright", "Python", "BasedPyright", "basedpyright-langserver", ["--stdio"], ["python"], CreateLanguageIds(".py", "python"), [".py"], 220, "pip install basedpyright", null, true, "built-in"),
            new LanguageServerDescriptor("python-pyright", "Python", "Pyright language server", "pyright-langserver", ["--stdio"], ["python"], CreateLanguageIds(".py", "python"), [".py"], 200, "npm install -g pyright", null, true, "built-in"),
            new LanguageServerDescriptor("python-pylsp", "Python", "Python LSP server", "pylsp", [], ["python"], CreateLanguageIds(".py", "python"), [".py"], 150, "pip install python-lsp-server", null, true, "built-in"),
            new LanguageServerDescriptor("rust-rust-analyzer", "Rust", "Rust analyzer", "rust-analyzer", [], ["rust"], CreateLanguageIds(".rs", "rust"), [".rs"], 200, "rustup component add rust-analyzer", null, true, "built-in"),
            new LanguageServerDescriptor("go-gopls", "Go", "Go language server", "gopls", [], ["go"], CreateLanguageIds(".go", "go"), [".go"], 200, "go install golang.org/x/tools/gopls@latest", null, true, "built-in"),
            new LanguageServerDescriptor("cpp-clangd", "C/C++", "Clangd", "clangd", [], ["c", "cpp"], CreateLanguageIds(".c", "c", ".h", "c", ".cc", "cpp", ".cpp", "cpp", ".cxx", "cpp", ".hh", "cpp", ".hpp", "cpp", ".hxx", "cpp"), [".c", ".h", ".cc", ".cpp", ".cxx", ".hh", ".hpp", ".hxx"], 200, "Install clangd and add it to PATH.", null, true, "built-in"),
            new LanguageServerDescriptor("java-jdtls", "Java", "JDTLS", "jdtls", [], ["java"], CreateLanguageIds(".java", "java"), [".java"], 200, "Install jdtls and add it to PATH.", null, true, "built-in"),
            new LanguageServerDescriptor("kotlin-kotlin-language-server", "Kotlin", "Kotlin language server", "kotlin-language-server", [], ["kotlin"], CreateLanguageIds(".kt", "kotlin", ".kts", "kotlin"), [".kt", ".kts"], 200, "Install Kotlin language server and add it to PATH.", null, true, "built-in"),
            new LanguageServerDescriptor("php-intelephense", "PHP", "Intelephense", "intelephense", ["--stdio"], ["php"], CreateLanguageIds(".php", "php"), [".php"], 200, "npm install -g intelephense", null, true, "built-in"),
            new LanguageServerDescriptor("php-phpactor", "PHP", "Phpactor", "phpactor", ["language-server"], ["php"], CreateLanguageIds(".php", "php"), [".php"], 150, "Install phpactor and add it to PATH.", null, true, "built-in"),
            new LanguageServerDescriptor("ruby-ruby-lsp", "Ruby", "Ruby LSP", "ruby-lsp", [], ["ruby"], CreateLanguageIds(".rb", "ruby"), [".rb"], 200, "gem install ruby-lsp", null, true, "built-in"),
            new LanguageServerDescriptor("ruby-solargraph", "Ruby", "Solargraph", "solargraph", ["stdio"], ["ruby"], CreateLanguageIds(".rb", "ruby"), [".rb"], 150, "gem install solargraph", null, true, "built-in"),
            new LanguageServerDescriptor("web-html", "HTML/CSS/JSON/YAML", "VS Code HTML language server", "vscode-html-language-server", ["--stdio"], ["html"], CreateLanguageIds(".html", "html", ".htm", "html"), [".html", ".htm"], 200, "npm install -g vscode-langservers-extracted", null, true, "built-in"),
            new LanguageServerDescriptor("web-css", "HTML/CSS/JSON/YAML", "VS Code CSS language server", "vscode-css-language-server", ["--stdio"], ["css"], CreateLanguageIds(".css", "css", ".scss", "scss", ".less", "less"), [".css", ".scss", ".less"], 200, "npm install -g vscode-langservers-extracted", null, true, "built-in"),
            new LanguageServerDescriptor("web-json", "HTML/CSS/JSON/YAML", "VS Code JSON language server", "vscode-json-language-server", ["--stdio"], ["json"], CreateLanguageIds(".json", "json", ".jsonc", "jsonc"), [".json", ".jsonc"], 200, "npm install -g vscode-langservers-extracted", null, true, "built-in"),
            new LanguageServerDescriptor("web-yaml", "HTML/CSS/JSON/YAML", "YAML language server", "yaml-language-server", ["--stdio"], ["yaml"], CreateLanguageIds(".yaml", "yaml", ".yml", "yaml"), [".yaml", ".yml"], 200, "npm install -g yaml-language-server", null, true, "built-in")
        ];
    }

    private static Dictionary<string, string> CreateLanguageIds(params string[] values)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index + 1 < values.Length; index += 2)
        {
            result[NormalizeExtension(values[index])] = values[index + 1];
        }

        return result;
    }
}

internal enum LanguageServerHealthState
{
    Missing,
    Detected,
    Healthy,
    Unhealthy,
    Disabled
}

internal sealed record LanguageServerProbeResult(
    LanguageServerHealthState State,
    string? ResolvedCommand,
    string? Message);

internal sealed record LanguageServerResolution(
    string Extension,
    string LanguageId,
    IReadOnlyList<ResolvedLanguageServer> Candidates);

internal sealed record ResolvedLanguageServer(
    LanguageServerDescriptor Descriptor,
    LanguageServerProbeResult Probe);

internal sealed record LanguageServerStatusEntry(
    string Language,
    string LanguageId,
    IReadOnlyList<string> FileExtensions,
    IReadOnlyList<LanguageServerCandidateStatus> Candidates,
    string? SelectedServerName);

internal sealed record LanguageServerCandidateStatus(
    string Key,
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    int Priority,
    string DetectionStatus,
    string Source,
    string? ResolvedCommand,
    string? InstallHint,
    string LanguageId,
    string? Message);

internal sealed class LanguageServerDescriptor
{
    public string Key { get; }

    public string Language { get; }

    public string Name { get; }

    public string Command { get; }

    public IReadOnlyList<string> Arguments { get; }

    public IReadOnlyList<string> Aliases { get; }

    public IReadOnlyDictionary<string, string> LanguageIdsByExtension { get; }

    public IReadOnlyList<string> FileExtensions { get; }

    public int Priority { get; }

    public string? InstallHint { get; }

    public JsonElement? InitializationOptions { get; }

    public bool Enabled { get; }

    public string Source { get; }

    public string LanguageId => LanguageIdsByExtension.Values.FirstOrDefault() ?? "text";

    public LanguageServerDescriptor(
        string key,
        string language,
        string name,
        string command,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> aliases,
        IReadOnlyDictionary<string, string> languageIdsByExtension,
        IReadOnlyList<string> fileExtensions,
        int priority,
        string? installHint,
        JsonElement? initializationOptions,
        bool enabled,
        string source)
    {
        Key = key;
        Language = language;
        Name = name;
        Command = command;
        Arguments = arguments;
        Aliases = aliases;
        LanguageIdsByExtension = languageIdsByExtension;
        FileExtensions = fileExtensions;
        Priority = priority;
        InstallHint = installHint;
        InitializationOptions = initializationOptions;
        Enabled = enabled;
        Source = source;
    }

    public string GetLanguageId(string extension)
    {
        return LanguageIdsByExtension.TryGetValue(extension, out string? languageId)
            ? languageId
            : LanguageId;
    }

    public LanguageServerDescriptor Apply(LanguageServerProfileConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string language = string.IsNullOrWhiteSpace(configuration.Language)
            ? Language
            : configuration.Language;
        string name = string.IsNullOrWhiteSpace(configuration.Name)
            ? Name
            : configuration.Name;
        string command = string.IsNullOrWhiteSpace(configuration.Command)
            ? Command
            : configuration.Command;
        IReadOnlyList<string> arguments = configuration.Args.Count > 0
            ? configuration.Args.ToArray()
            : Arguments;
        IReadOnlyList<string> extensions = configuration.FileExtensions.Count > 0
            ? configuration.FileExtensions
                .Select(static extension => extension.StartsWith(".", StringComparison.Ordinal)
                    ? extension.ToLowerInvariant()
                    : $".{extension.ToLowerInvariant()}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : FileExtensions;
        string languageId = string.IsNullOrWhiteSpace(configuration.LanguageId)
            ? LanguageId
            : configuration.LanguageId;
        Dictionary<string, string> languageIdsByExtension = extensions.ToDictionary(
            static extension => extension,
            _ => languageId,
            StringComparer.OrdinalIgnoreCase);

        return new LanguageServerDescriptor(
            configuration.Key,
            language,
            name,
            command,
            arguments,
            Aliases,
            languageIdsByExtension,
            extensions,
            configuration.Priority ?? Priority,
            configuration.InstallHint ?? InstallHint,
            configuration.InitializationOptions ?? InitializationOptions,
            configuration.Enabled ?? Enabled,
            configuration.SourcePath is null ? Source : "profile");
    }

    public static LanguageServerDescriptor FromConfiguration(LanguageServerProfileConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string language = string.IsNullOrWhiteSpace(configuration.Language)
            ? configuration.Key
            : configuration.Language;
        string name = string.IsNullOrWhiteSpace(configuration.Name)
            ? configuration.Key
            : configuration.Name;
        string command = configuration.Command ?? configuration.Key;
        string languageId = string.IsNullOrWhiteSpace(configuration.LanguageId)
            ? language.ToLowerInvariant().Replace(" ", "-")
            : configuration.LanguageId;
        IReadOnlyList<string> extensions = configuration.FileExtensions
            .Select(static extension => extension.StartsWith(".", StringComparison.Ordinal)
                ? extension.ToLowerInvariant()
                : $".{extension.ToLowerInvariant()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Dictionary<string, string> languageIds = extensions.ToDictionary(
            static extension => extension,
            _ => languageId,
            StringComparer.OrdinalIgnoreCase);

        return new LanguageServerDescriptor(
            configuration.Key,
            language,
            name,
            command,
            configuration.Args.ToArray(),
            [],
            languageIds,
            extensions,
            configuration.Priority ?? 100,
            configuration.InstallHint,
            configuration.InitializationOptions,
            configuration.Enabled ?? true,
            "profile");
    }
}
