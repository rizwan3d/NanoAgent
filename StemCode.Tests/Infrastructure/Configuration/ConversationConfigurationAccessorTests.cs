using FluentAssertions;
using Microsoft.Extensions.Options;
using StemCode.Application.Models;
using StemCode.Infrastructure.Configuration;

namespace StemCode.Tests.Infrastructure.Configuration;

public sealed class ConversationConfigurationAccessorTests
{
    [Fact]
    public void GetSettings_Should_ReturnInfiniteTimeout_When_RequestTimeoutSecondsIsZero()
    {
        IOptions<ApplicationOptions> options = Options.Create(new ApplicationOptions
        {
            Conversation = new ConversationOptions
            {
                RequestTimeoutSeconds = 0,
                SystemPrompt = "  test prompt  "
            }
        });

        ConversationConfigurationAccessor sut = new(options);

        ConversationSettings result = sut.GetSettings();

        result.SystemPrompt.Should().StartWith(ConversationOptions.IdentityDescription);
        result.SystemPrompt.Should().EndWith("test prompt");
        result.RequestTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        result.MaxToolRoundsPerTurn.Should().Be(0);
    }
}
