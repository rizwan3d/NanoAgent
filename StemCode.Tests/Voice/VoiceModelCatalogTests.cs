using FluentAssertions;
using StemCode.Voice;
using Whisper.net.Ggml;
using Xunit;

namespace StemCode.Tests.Voice;

public sealed class VoiceModelCatalogTests
{
    [Fact]
    public void Catalog_Should_ExposeThreeLogicalModels()
    {
        IReadOnlyList<VoiceModelSpec> specs = VoiceModelCatalog.All;

        specs.Should().HaveCount(3);
        specs.Select(spec => spec.Id).Should().ContainInOrder("fast", "balanced", "accurate");
    }

    [Fact]
    public void Catalog_Should_MarkBalancedAsRecommended()
    {
        VoiceModelCatalog.Default.Id.Should().Be("balanced");
        VoiceModelCatalog.All.Should().ContainSingle(spec => spec.IsRecommended);
        VoiceModelCatalog.All.Single(spec => spec.IsRecommended).Id.Should().Be("balanced");
    }

    [Theory]
    [InlineData("fast", GgmlType.TinyEn)]
    [InlineData("balanced", GgmlType.SmallEn)]
    [InlineData("accurate", GgmlType.MediumEn)]
    public void TryGet_Should_MapLogicalIdToWhisperModel(string id, GgmlType expected)
    {
        bool resolved = VoiceModelCatalog.TryGet(id, out VoiceModelSpec spec);

        resolved.Should().BeTrue();
        spec.GgmlType.Should().Be(expected);
        spec.Quantization.Should().Be(QuantizationType.Q5_0);
    }

    [Fact]
    public void TryGet_Should_FallBackToDefaultForUnknownId()
    {
        bool resolved = VoiceModelCatalog.TryGet("does-not-exist", out VoiceModelSpec spec);

        resolved.Should().BeFalse();
        spec.Id.Should().Be("balanced");
    }

    [Fact]
    public void ModelPath_Should_ResolveToBinFileUnderModelsDirectory()
    {
        string path = VoiceModelCatalog.ModelPath("balanced");

        Path.GetFileName(path).Should().Be("balanced.bin");
        Path.GetDirectoryName(path).Should().Be(VoiceModelCatalog.ModelsDirectory);
    }

    [Fact]
    public void ModelPath_Should_SanitizeUnsafeIdentifiers()
    {
        string path = VoiceModelCatalog.ModelPath("a/b\\c:*?");

        string fileName = Path.GetFileName(path);
        fileName.Should().NotContain("/");
        fileName.Should().NotContain("\\");
        fileName.Should().EndWith(".bin");
    }
}
