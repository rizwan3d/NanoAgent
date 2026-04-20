using NanoAgent.Application.Abstractions;
using NanoAgent.Application.Models;
using NanoAgent.Domain.Models;
using NanoAgent.Infrastructure.Storage;
using FluentAssertions;

namespace NanoAgent.Tests.Infrastructure.Storage;

public sealed class JsonConversationSectionStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public JsonConversationSectionStoreTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NanoAgent-Sections-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_Should_RoundTripSectionSnapshot()
    {
        StubUserDataPathProvider pathProvider = new(_tempRoot);
        JsonConversationSectionStore sut = new(pathProvider);
        ConversationSectionSnapshot snapshot = new(
            Guid.NewGuid().ToString("D"),
            "Todo App Session",
            new DateTimeOffset(2026, 4, 21, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 21, 1, 5, 0, TimeSpan.Zero),
            new AgentProviderProfile(ProviderKind.OpenAiCompatible, "https://provider.example.com/v1"),
            "gpt-5-mini",
            ["gpt-5-mini", "gpt-4.1"],
            [new ConversationSectionTurn("build a todo app", "I created the scaffold.")],
            27);

        await sut.SaveAsync(snapshot, CancellationToken.None);
        ConversationSectionSnapshot? loadedSnapshot = await sut.LoadAsync(snapshot.SectionId, CancellationToken.None);

        loadedSnapshot.Should().NotBeNull();
        loadedSnapshot!.SectionId.Should().Be(snapshot.SectionId);
        loadedSnapshot.Title.Should().Be("Todo App Session");
        loadedSnapshot.ActiveModelId.Should().Be("gpt-5-mini");
        loadedSnapshot.AvailableModelIds.Should().Equal("gpt-5-mini", "gpt-4.1");
        loadedSnapshot.ProviderProfile.Should().Be(snapshot.ProviderProfile);
        loadedSnapshot.Turns.Should().ContainSingle();
        loadedSnapshot.Turns[0].UserInput.Should().Be("build a todo app");
        loadedSnapshot.Turns[0].AssistantResponse.Should().Be("I created the scaffold.");
        loadedSnapshot.TotalEstimatedOutputTokens.Should().Be(27);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class StubUserDataPathProvider : IUserDataPathProvider
    {
        private readonly string _root;

        public StubUserDataPathProvider(string root)
        {
            _root = root;
        }

        public string GetConfigurationFilePath()
        {
            return Path.Combine(_root, "agent-profile.json");
        }

        public string GetLogsDirectoryPath()
        {
            return Path.Combine(_root, "logs");
        }

        public string GetSectionsDirectoryPath()
        {
            return Path.Combine(_root, "sections");
        }
    }
}
