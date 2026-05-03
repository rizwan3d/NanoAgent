using FluentAssertions;
using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.BudgetControls;
using System.Net;
using System.Text.Json;

namespace NanoAgent.Tests.Infrastructure.BudgetControls;

public sealed class BudgetControlsUsageServiceTests
{
    [Fact]
    public async Task RecordUsageAsync_Should_UpdateLocalUsageAndCost()
    {
        string workspacePath = CreateWorkspace();
        InMemoryBudgetConfigurationStore configurationStore = new();
        BudgetControlsUsageService sut = new(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            configurationStore,
            new InMemoryBudgetSecretStore());
        ReplSessionContext session = CreateSession(workspacePath);

        await sut.ConfigureLocalAsync(
            session,
            ".nanoagent/budget-controls.local.json",
            new BudgetControlsLocalOptions(
                new BudgetControlsPricing(
                    InputUsdPerMillionTokens: 1m,
                    CachedInputUsdPerMillionTokens: 0.1m,
                    OutputUsdPerMillionTokens: 2m),
                MonthlyBudgetUsd: 50m,
                AlertThresholdPercent: 70),
            CancellationToken.None);

        await sut.RecordUsageAsync(
            session,
            new BudgetControlsUsageDelta(
                InputTokens: 1_000,
                CachedInputTokens: 200,
                OutputTokens: 500),
            CancellationToken.None);

        string budgetPath = Path.Combine(workspacePath, ".nanoagent", "budget-controls.local.json");
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(budgetPath));
        JsonElement root = document.RootElement;
        JsonElement usage = root.GetProperty("usage");

        usage.GetProperty("inputTokens").GetInt64().Should().Be(1_000);
        usage.GetProperty("cachedInputTokens").GetInt64().Should().Be(200);
        usage.GetProperty("outputTokens").GetInt64().Should().Be(500);
        usage.GetProperty("totalCostUsd").GetDecimal().Should().Be(0.00182m);
        root.GetProperty("monthlyBudgetUsd").GetDecimal().Should().Be(50m);
        root.GetProperty("alertThresholdPercent").GetInt32().Should().Be(70);
        root.GetProperty("spentUsd").GetDecimal().Should().Be(0.00182m);
        configurationStore.Settings!.Source.Should().Be(BudgetControlsSettings.LocalSource);
    }

    [Fact]
    public async Task GetStatusAsync_Should_ReadCloudBudgetStatus()
    {
        InMemoryBudgetConfigurationStore configurationStore = new()
        {
            Settings = BudgetControlsSettings.Cloud("https://budget.example.test/usage", hasCloudAuthKey: true)
        };
        InMemoryBudgetSecretStore secretStore = new()
        {
            AuthKey = "secret"
        };
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"monthlyBudgetUsd":100,"spentUsd":25.5,"alertThresholdPercent":75}""")
        });
        BudgetControlsUsageService sut = new(
            new HttpClient(handler),
            configurationStore,
            secretStore);

        BudgetControlsStatus status = await sut.GetStatusAsync(
            CreateSession(CreateWorkspace()),
            CancellationToken.None);

        status.Source.Should().Be(BudgetControlsSettings.CloudSource);
        status.MonthlyBudgetUsd.Should().Be(100m);
        status.SpentUsd.Should().Be(25.5m);
        status.AlertThresholdPercent.Should().Be(75);
        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Authorization.Should().NotBeNull();
        request.Authorization!.Scheme.Should().Be("Bearer");
        request.Authorization.Parameter.Should().Be("secret");
    }

    [Fact]
    public async Task RecordUsageAsync_Should_PostOnlyLastCloudUsageDelta()
    {
        InMemoryBudgetConfigurationStore configurationStore = new()
        {
            Settings = BudgetControlsSettings.Cloud("https://budget.example.test/usage", hasCloudAuthKey: true)
        };
        InMemoryBudgetSecretStore secretStore = new()
        {
            AuthKey = "secret"
        };
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"monthlyBudgetUsd":100,"spentUsd":26,"alertThresholdPercent":75}""")
        });
        BudgetControlsUsageService sut = new(
            new HttpClient(handler),
            configurationStore,
            secretStore);

        await sut.RecordUsageAsync(
            CreateSession(CreateWorkspace()),
            new BudgetControlsUsageDelta(
                InputTokens: 123,
                CachedInputTokens: 45,
                OutputTokens: 67),
            CancellationToken.None);

        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("inputTokens").GetInt32().Should().Be(123);
        body.RootElement.GetProperty("cachedInputTokens").GetInt32().Should().Be(45);
        body.RootElement.GetProperty("outputTokens").GetInt32().Should().Be(67);
    }

    private static ReplSessionContext CreateSession(string workspacePath)
    {
        return new ReplSessionContext(
            new AgentProviderProfile(ProviderKind.OpenAi, null),
            "gpt-test",
            ["gpt-test"],
            workspacePath: workspacePath);
    }

    private static string CreateWorkspace()
    {
        string workspacePath = Path.Combine(
            Path.GetTempPath(),
            "nanoagent-budget-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private sealed class InMemoryBudgetConfigurationStore : IBudgetControlsConfigurationStore
    {
        public BudgetControlsSettings? Settings { get; set; }

        public Task<BudgetControlsSettings?> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(
            BudgetControlsSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryBudgetSecretStore : IBudgetControlsSecretStore
    {
        public string? AuthKey { get; set; }

        public Task<string?> LoadCloudAuthKeyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AuthKey);
        }

        public Task SaveCloudAuthKeyAsync(
            string authKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthKey = authKey;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handle;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        {
            _handle = handle;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                body));

            return _handle(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization,
        string? Body);
}
