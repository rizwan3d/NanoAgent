using System.Text.Json;
using FluentAssertions;
using StemCode.Application.Voice;

namespace StemCode.Tests.CLI;

public sealed class VoiceSerializationTests
{
    [Fact]
    public void VoiceSettings_Should_RoundTripThroughSourceGenContext()
    {
        VoiceSettings settings = new("accurate", "mic-1");

        string json = JsonSerializer.Serialize(settings, VoiceJsonContext.Default.VoiceSettings);
        VoiceSettings? restored = JsonSerializer.Deserialize(
            json,
            VoiceJsonContext.Default.VoiceSettings);

        restored.Should().NotBeNull();
        restored!.ModelId.Should().Be("accurate");
        restored.InputDeviceId.Should().Be("mic-1");
        restored.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void VoiceModelOptions_Should_DeserializeLowercaseRuntimeJson()
    {
        // The voice runtime emits lowercase property names.
        const string json = """
            [
              {"id":"fast","label":"Fast","description":"Small download.","isRecommended":false},
              {"id":"balanced","label":"Balanced","description":"Recommended balance.","isRecommended":true}
            ]
            """;

        VoiceModelOption[]? models = JsonSerializer.Deserialize(
            json,
            VoiceJsonContext.Default.VoiceModelOptionArray);

        models.Should().NotBeNull();
        models!.Should().HaveCount(2);
        models[0].Id.Should().Be("fast");
        models[0].Label.Should().Be("Fast");
        models[1].IsRecommended.Should().BeTrue();
    }

    [Fact]
    public void VoiceInputDevices_Should_DeserializeLowercaseRuntimeJson()
    {
        const string json = """
            [
              {"id":"","name":"System default","isDefault":true},
              {"id":"mic-1","name":"USB Microphone","isDefault":false}
            ]
            """;

        VoiceInputDevice[]? devices = JsonSerializer.Deserialize(
            json,
            VoiceJsonContext.Default.VoiceInputDeviceArray);

        devices.Should().NotBeNull();
        devices!.Should().HaveCount(2);
        devices[0].IsDefault.Should().BeTrue();
        devices[1].Name.Should().Be("USB Microphone");
    }
}
