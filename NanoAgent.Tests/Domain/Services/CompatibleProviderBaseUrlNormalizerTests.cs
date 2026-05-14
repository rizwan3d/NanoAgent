using FluentAssertions;
using NanoAgent.Domain.Services;

namespace NanoAgent.Tests.Domain.Services;

public sealed class CompatibleProviderBaseUrlNormalizerTests
{
    [Fact]
    public void Normalize_Should_NormalizeTrailingSlash_When_StringProvided()
    {
        string result = CompatibleProviderBaseUrlNormalizer.Normalize("https://provider.example.com/v1/");

        result.Should().Be("https://provider.example.com/v1");
    }

    [Fact]
    public void Normalize_Should_TrimAndNormalize_When_WhitespaceSurrounds()
    {
        string result = CompatibleProviderBaseUrlNormalizer.Normalize("  https://provider.example.com/v1/  ");

        result.Should().Be("https://provider.example.com/v1");
    }

    [Fact]
    public void Normalize_Should_AppendV1_When_RootPath()
    {
        string result = CompatibleProviderBaseUrlNormalizer.Normalize("http://127.0.0.1:1234");

        result.Should().Be("http://127.0.0.1:1234/v1");
    }

    [Fact]
    public void Normalize_Should_AppendV1_When_SlashOnlyPath()
    {
        string result = CompatibleProviderBaseUrlNormalizer.Normalize("http://127.0.0.1:1234/");

        result.Should().Be("http://127.0.0.1:1234/v1");
    }

    [Fact]
    public void Normalize_Should_StripQueryAndFragment()
    {
        string result = CompatibleProviderBaseUrlNormalizer.Normalize("https://api.example.com/v1?key=value#frag");

        result.Should().Be("https://api.example.com/v1");
    }

    [Fact]
    public void Normalize_Should_Throw_When_StringIsEmpty()
    {
        Action act = () => CompatibleProviderBaseUrlNormalizer.Normalize("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_Should_Throw_When_StringIsWhitespace()
    {
        Action act = () => CompatibleProviderBaseUrlNormalizer.Normalize("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_Should_Throw_When_UrlIsNotAbsolute()
    {
        Action act = () => CompatibleProviderBaseUrlNormalizer.Normalize("not-a-url");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Normalize_WithUri_Should_Throw_When_UriIsNull()
    {
        Action act = () => CompatibleProviderBaseUrlNormalizer.Normalize((Uri)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Normalize_WithUri_Should_Throw_When_SchemeIsNotHttpOrHttps()
    {
        Action act = () => CompatibleProviderBaseUrlNormalizer.Normalize(new Uri("ftp://example.com/v1"));

        act.Should().Throw<ArgumentException>().WithMessage("*http or https*");
    }

    [Fact]
    public void Normalize_WithUri_Should_ReturnNormalized()
    {
        string result = CompatibleProviderBaseUrlNormalizer.Normalize(new Uri("https://api.example.com/v1/"));

        result.Should().Be("https://api.example.com/v1");
    }
}
