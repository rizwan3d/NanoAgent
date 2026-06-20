using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Infrastructure.Plugins;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.Plugins;

public sealed class PluginServiceTests : IDisposable
{
    private const string Repository = "DietrichGebert/ponytail";
    private const string PluginId = "ponytail";
    private readonly string _workspaceRoot;

    public PluginServiceTests()
    {
        _workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task AddMarketplaceAsync_Should_WriteExpectedJson()
    {
        PluginService sut = CreateSut();

        PluginMarketplaceAddResult result = await sut.AddMarketplaceAsync(
            _workspaceRoot,
            Repository,
            alias: null,
            reference: null,
            CancellationToken.None);

        result.Alias.Should().Be("ponytail");
        PluginMarketplaceConfig config = await ReadMarketplacesAsync();
        config.Marketplaces.Should().ContainKey("ponytail");
        config.Marketplaces["ponytail"].Repository.Should().Be(Repository);
        config.Marketplaces["ponytail"].Ref.Should().Be("main");
    }

    [Fact]
    public async Task AddMarketplaceAsync_Should_RespectAliasAndRef()
    {
        PluginService sut = CreateSut();

        PluginMarketplaceAddResult result = await sut.AddMarketplaceAsync(
            _workspaceRoot,
            Repository,
            alias: "tail",
            reference: "stable",
            CancellationToken.None);

        result.Alias.Should().Be("tail");
        PluginMarketplaceConfig config = await ReadMarketplacesAsync();
        config.Marketplaces.Should().ContainKey("tail");
        config.Marketplaces["tail"].Ref.Should().Be("stable");
    }

    [Fact]
    public async Task InstallAsync_Should_RejectUnknownMarketplace()
    {
        PluginService sut = CreateSut();

        Func<Task> action = () => sut.InstallAsync(
            _workspaceRoot,
            PluginId,
            "missing",
            force: false,
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*marketplace 'missing'*");
    }

    [Fact]
    public async Task InstallAsync_Should_RejectPathTraversalInManifestTarget()
    {
        StubHttpMessageHandler handler = new();
        handler.AddJson(
            Repository,
            "main",
            "nanoagent-plugin.json",
            """
            {
              "id": "ponytail",
              "files": [
                {
                  "source": "skills/ponytail/SKILL.md",
                  "target": "../escape.txt",
                  "kind": "skill"
                }
              ]
            }
            """);
        PluginService sut = CreateSut(handler);
        await sut.AddMarketplaceAsync(_workspaceRoot, Repository, alias: null, reference: null, CancellationToken.None);

        Func<Task> action = () => sut.InstallAsync(
            _workspaceRoot,
            PluginId,
            "ponytail",
            force: false,
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*not allowed*");
    }

    [Fact]
    public async Task InstallAsync_Should_NotOverwriteExistingFilesWithoutForce()
    {
        StubHttpMessageHandler handler = CreatePonytailFallbackHandler();
        PluginService sut = CreateSut(handler);
        await sut.AddMarketplaceAsync(_workspaceRoot, Repository, alias: null, reference: null, CancellationToken.None);
        WriteFile(".nanoagent/skills/ponytail/SKILL.md", "existing");

        Func<Task> action = () => sut.InstallAsync(
            _workspaceRoot,
            PluginId,
            "ponytail",
            force: false,
            CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
        ReadFile(".nanoagent/skills/ponytail/SKILL.md").Should().Be("existing");
    }

    [Fact]
    public async Task UninstallAsync_Should_RemoveOnlyLockOwnedFiles()
    {
        PluginService sut = CreateSut();
        WriteFile(".nanoagent/plugins/ponytail/AGENTS.md", "tracked");
        WriteFile(".nanoagent/plugins/ponytail/README.md", "keep");
        await WriteInstalledLockAsync(new InstalledPluginLock
        {
            Plugins = new Dictionary<string, InstalledPluginEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [PluginId] = new()
                {
                    PluginId = PluginId,
                    MarketplaceAlias = "ponytail",
                    Repository = Repository,
                    Ref = "main",
                    Files = [".nanoagent/plugins/ponytail/AGENTS.md"]
                }
            }
        });

        PluginUninstallResult result = await sut.UninstallAsync(
            _workspaceRoot,
            PluginId,
            CancellationToken.None);

        result.RemovedFiles.Should().ContainSingle(".nanoagent/plugins/ponytail/AGENTS.md");
        File.Exists(GetPath(".nanoagent/plugins/ponytail/AGENTS.md")).Should().BeFalse();
        File.Exists(GetPath(".nanoagent/plugins/ponytail/README.md")).Should().BeTrue();
        InstalledPluginLock lockFile = await ReadInstalledLockAsync();
        lockFile.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task InstallAsync_Should_UseConventionFallbackForPonytail()
    {
        StubHttpMessageHandler handler = CreatePonytailFallbackHandler();
        PluginService sut = CreateSut(handler);
        await sut.AddMarketplaceAsync(_workspaceRoot, Repository, alias: null, reference: null, CancellationToken.None);

        PluginInstallResult result = await sut.InstallAsync(
            _workspaceRoot,
            PluginId,
            "ponytail",
            force: false,
            CancellationToken.None);

        result.UsedManifest.Should().BeFalse();
        ReadFile(".nanoagent/skills/ponytail/SKILL.md").Should().Be("skill body");
        ReadFile(".nanoagent/plugins/ponytail/AGENTS.md").Should().Be("agent body");

        InstalledPluginLock lockFile = await ReadInstalledLockAsync();
        lockFile.Plugins.Should().ContainKey("ponytail");
        lockFile.Plugins["ponytail"].Files.Should().BeEquivalentTo(
            ".nanoagent/skills/ponytail/SKILL.md",
            ".nanoagent/plugins/ponytail/AGENTS.md");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private PluginService CreateSut(StubHttpMessageHandler? handler = null)
    {
        return new PluginService(new HttpClient(handler ?? new StubHttpMessageHandler()));
    }

    private static StubHttpMessageHandler CreatePonytailFallbackHandler()
    {
        StubHttpMessageHandler handler = new();
        handler.AddStatus(Repository, "main", "nanoagent-plugin.json", HttpStatusCode.NotFound);
        handler.AddText(Repository, "main", "skills/ponytail/SKILL.md", "skill body");
        handler.AddText(Repository, "main", "AGENTS.md", "agent body");
        return handler;
    }

    private async Task<PluginMarketplaceConfig> ReadMarketplacesAsync()
    {
        string json = await File.ReadAllTextAsync(GetPath(".nanoagent/plugins/marketplaces.json"));
        return JsonSerializer.Deserialize(
                   json,
                   PluginJsonContext.Default.PluginMarketplaceConfig)
               ?? new PluginMarketplaceConfig();
    }

    private async Task<InstalledPluginLock> ReadInstalledLockAsync()
    {
        string json = await File.ReadAllTextAsync(GetPath(".nanoagent/plugins/installed.json"));
        return JsonSerializer.Deserialize(
                   json,
                   PluginJsonContext.Default.InstalledPluginLock)
               ?? new InstalledPluginLock();
    }

    private async Task WriteInstalledLockAsync(InstalledPluginLock lockFile)
    {
        string path = GetPath(".nanoagent/plugins/installed.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(lockFile, PluginJsonContext.Default.InstalledPluginLock);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private void WriteFile(string relativePath, string content)
    {
        string path = GetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string ReadFile(string relativePath)
    {
        return File.ReadAllText(GetPath(relativePath));
    }

    private string GetPath(string relativePath)
    {
        return Path.Combine(
            _workspaceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode StatusCode, string Body)> _responses =
            new(StringComparer.Ordinal);

        public void AddJson(
            string repository,
            string reference,
            string sourcePath,
            string body)
        {
            Add(repository, reference, sourcePath, HttpStatusCode.OK, body, "application/json");
        }

        public void AddText(
            string repository,
            string reference,
            string sourcePath,
            string body)
        {
            Add(repository, reference, sourcePath, HttpStatusCode.OK, body, "text/plain");
        }

        public void AddStatus(
            string repository,
            string reference,
            string sourcePath,
            HttpStatusCode statusCode)
        {
            Add(repository, reference, sourcePath, statusCode, string.Empty, "text/plain");
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            (HttpStatusCode statusCode, string body) response = _responses.TryGetValue(url, out var value)
                ? value
                : (HttpStatusCode.NotFound, string.Empty);

            return Task.FromResult(new HttpResponseMessage(response.statusCode)
            {
                Content = new StringContent(response.body)
            });
        }

        private void Add(
            string repository,
            string reference,
            string sourcePath,
            HttpStatusCode statusCode,
            string body,
            string mediaType)
        {
            string url = BuildRawUrl(repository, reference, sourcePath);
            _responses[url] = (statusCode, body);
        }

        private static string BuildRawUrl(
            string repository,
            string reference,
            string sourcePath)
        {
            string[] repoParts = repository.Split('/');
            string encodedRef = Uri.EscapeDataString(reference);
            string encodedPath = string.Join(
                "/",
                sourcePath
                    .Replace("\\", "/", StringComparison.Ordinal)
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));

            return $"https://raw.githubusercontent.com/{Uri.EscapeDataString(repoParts[0])}/{Uri.EscapeDataString(repoParts[1])}/{encodedRef}/{encodedPath}";
        }
    }
}
