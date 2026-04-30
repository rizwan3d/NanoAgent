using FluentAssertions;
using NanoAgent.Application.Models;
using NanoAgent.Application.Services;

namespace NanoAgent.Tests.Application.Services;

public sealed class OnboardingInputValidatorTests
{
    private readonly OnboardingInputValidator _sut = new();

    [Fact]
    public void ValidateApiKey_Should_ReturnSuccess_When_ValueContainsText()
    {
        InputValidationResult result = _sut.ValidateApiKey("  test-key  ");

        result.Should().Be(InputValidationResult.Success("test-key"));
    }

    [Fact]
    public void ValidateApiKey_Should_ReturnFailure_When_ValueIsWhitespace()
    {
        InputValidationResult result = _sut.ValidateApiKey("   ");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("API key cannot be empty.");
    }

    [Fact]
    public void ValidateBaseUrl_Should_ReturnNormalizedUrl_When_HttpOrHttpsUrlIsValid()
    {
        InputValidationResult result = _sut.ValidateBaseUrl(" https://provider.example.com/v1/ ");

        result.Should().Be(InputValidationResult.Success("https://provider.example.com/v1"));
    }

    [Fact]
    public void ValidateBaseUrl_Should_AppendV1_When_RootCompatibleBaseUrlIsProvided()
    {
        InputValidationResult result = _sut.ValidateBaseUrl(" http://127.0.0.1:1234/ ");

        result.Should().Be(InputValidationResult.Success("http://127.0.0.1:1234/v1"));
    }

    [Fact]
    public void ValidateBaseUrl_Should_ReturnFailure_When_UrlContainsQueryString()
    {
        InputValidationResult result = _sut.ValidateBaseUrl("https://provider.example.com/v1?model=gpt");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Base URL cannot contain a query string or fragment.");
    }

    [Fact]
    public void ValidateBaseUrl_Should_ReturnFailure_When_UrlUsesUnsupportedScheme()
    {
        InputValidationResult result = _sut.ValidateBaseUrl("ftp://provider.example.com/v1");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Base URL must use http or https.");
    }
}
