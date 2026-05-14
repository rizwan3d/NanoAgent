using FluentAssertions;
using NanoAgent.Application.Models;

namespace NanoAgent.Tests.Application.Models;

public sealed class ConversationTurnMetricsTests
{
    [Fact]
    public void Constructor_Should_Set_Properties()
    {
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(30),
            1500,
            sessionEstimatedOutputTokens: 3000,
            estimatedInputTokens: 500,
            cachedInputTokens: 100,
            providerRetryCount: 2,
            toolRoundCount: 3);

        metrics.Elapsed.Should().Be(TimeSpan.FromSeconds(30));
        metrics.EstimatedOutputTokens.Should().Be(1500);
        metrics.SessionEstimatedOutputTokens.Should().Be(3000);
        metrics.EstimatedInputTokens.Should().Be(500);
        metrics.CachedInputTokens.Should().Be(100);
        metrics.ProviderRetryCount.Should().Be(2);
        metrics.ToolRoundCount.Should().Be(3);
    }

    [Fact]
    public void EstimatedTotalTokens_Should_Sum_Input_And_Output()
    {
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(10), 200, estimatedInputTokens: 100);

        metrics.EstimatedTotalTokens.Should().Be(300);
    }

    [Fact]
    public void DisplayedEstimatedOutputTokens_Should_UseSessionValue_When_Available()
    {
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(10), 200, sessionEstimatedOutputTokens: 500);

        metrics.DisplayedEstimatedOutputTokens.Should().Be(500);
    }

    [Fact]
    public void DisplayedEstimatedOutputTokens_Should_FallbackToOutputTokens()
    {
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(10), 200);

        metrics.DisplayedEstimatedOutputTokens.Should().Be(200);
    }

    [Fact]
    public void ToDisplayText_Should_ReturnFormattedString()
    {
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(30), 1500);

        string text = metrics.ToDisplayText();

        text.Should().Be("(30s · 1.5k tokens est.)");
    }

    [Fact]
    public void WithSessionEstimatedOutputTokens_Should_Return_NewInstance()
    {
        var metrics = new ConversationTurnMetrics(
            TimeSpan.FromSeconds(10), 200);

        var updated = metrics.WithSessionEstimatedOutputTokens(500);

        updated.SessionEstimatedOutputTokens.Should().Be(500);
        updated.EstimatedOutputTokens.Should().Be(200);
        updated.Elapsed.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Constructor_Should_Throw_When_ElapsedIsNegative()
    {
        Action act = () => new ConversationTurnMetrics(
            TimeSpan.FromSeconds(-1), 100);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_Should_Throw_When_EstimatedOutputTokensIsNegative()
    {
        Action act = () => new ConversationTurnMetrics(
            TimeSpan.Zero, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_Should_Throw_When_SessionEstimatedOutputTokensIsNegative()
    {
        Action act = () => new ConversationTurnMetrics(
            TimeSpan.Zero, 100, sessionEstimatedOutputTokens: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_Should_Throw_When_EstimatedInputTokensIsNegative()
    {
        Action act = () => new ConversationTurnMetrics(
            TimeSpan.Zero, 100, estimatedInputTokens: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
