using FluentAssertions;
using Microsoft.Extensions.Options;
using NanoAgent.Infrastructure.Configuration;

namespace NanoAgent.Tests.Infrastructure.Configuration;

public sealed class ApplicationOptionsValidatorTests
{
    private readonly ApplicationOptionsValidator _sut = new();

    [Fact]
    public void Validate_Should_ReturnSuccess_When_ApplicationOptionsAreValid()
    {
        ApplicationOptions options = new()
        {
            Defaults = new ApplicationDefaultsOptions(),
            ModelSelection = new ModelSelectionOptions
            {
                CacheDurationSeconds = 300
            }
        };

        ValidateOptionsResult result = _sut.Validate(Options.DefaultName, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ThrowArgumentNullException_When_OptionsAreNull()
    {
        Action act = () => _sut.Validate(Options.DefaultName, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Validate_Should_ReturnFailure_When_ModelSelectionSettingsAreInvalid()
    {
        ApplicationOptions options = new()
        {
            Defaults = new ApplicationDefaultsOptions(),
            ModelSelection = new ModelSelectionOptions
            {
                CacheDurationSeconds = 0
            }
        };

        ValidateOptionsResult result = _sut.Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("CacheDurationSeconds"));
    }

    [Fact]
    public void Validate_Should_ReturnSuccess_When_ConversationTimeoutIsZero()
    {
        ApplicationOptions options = new()
        {
            Conversation = new ConversationOptions
            {
                RequestTimeoutSeconds = 0
            },
            Defaults = new ApplicationDefaultsOptions(),
            ModelSelection = new ModelSelectionOptions
            {
                CacheDurationSeconds = 300
            }
        };

        ValidateOptionsResult result = _sut.Validate(Options.DefaultName, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnFailure_When_ConversationTimeoutIsNegative()
    {
        ApplicationOptions options = new()
        {
            Conversation = new ConversationOptions
            {
                RequestTimeoutSeconds = -1
            },
            Defaults = new ApplicationDefaultsOptions(),
            ModelSelection = new ModelSelectionOptions
            {
                CacheDurationSeconds = 300
            }
        };

        ValidateOptionsResult result = _sut.Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("RequestTimeoutSeconds"));
    }

    [Fact]
    public void Validate_Should_ReturnSuccess_When_MaxToolRoundsPerTurnIsZero()
    {
        ApplicationOptions options = new()
        {
            Conversation = new ConversationOptions
            {
                MaxToolRoundsPerTurn = 0
            },
            Defaults = new ApplicationDefaultsOptions(),
            ModelSelection = new ModelSelectionOptions
            {
                CacheDurationSeconds = 300
            }
        };

        ValidateOptionsResult result = _sut.Validate(Options.DefaultName, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReturnFailure_When_MaxToolRoundsPerTurnIsNegative()
    {
        ApplicationOptions options = new()
        {
            Conversation = new ConversationOptions
            {
                MaxToolRoundsPerTurn = -1
            },
            Defaults = new ApplicationDefaultsOptions(),
            ModelSelection = new ModelSelectionOptions
            {
                CacheDurationSeconds = 300
            }
        };

        ValidateOptionsResult result = _sut.Validate(Options.DefaultName, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("MaxToolRoundsPerTurn"));
    }
}
