using Whisper.net;
using Whisper.net.Ggml;

namespace StemCode.Voice;

/// <summary>
/// Downloads (when missing) and runs Whisper models for transcription.
/// Model binaries are fetched automatically from the public Whisper.net
/// model repository on first use.
/// </summary>
internal static class WhisperTranscriber
{
    public static async Task EnsureModelAsync(VoiceModelSpec spec, CancellationToken cancellationToken)
    {
        string path = VoiceModelCatalog.ModelPath(spec.Id);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return;
        }

        Directory.CreateDirectory(VoiceModelCatalog.ModelsDirectory);
        string temporaryPath = path + ".download";

        VoiceProtocol.WriteProgress(stage: "downloading", message: $"Downloading {spec.Label} voice model");
        await using (Stream source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                        spec.GgmlType, spec.Quantization, cancellationToken))
        await using (FileStream destination = File.Create(temporaryPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
        VoiceProtocol.WriteProgress(stage: "downloading", fraction: 1.0, message: "Voice model ready");
    }

    public static async Task<string> TranscribeAsync(
        VoiceModelSpec spec,
        float[] samples,
        CancellationToken cancellationToken)
    {
        string path = VoiceModelCatalog.ModelPath(spec.Id);

        using var factory = WhisperFactory.FromPath(path);
        using var processor = factory.CreateBuilder().WithLanguage("en").Build();

        await using Stream waveStream = CreateWavStream(samples);
        var transcript = new System.Text.StringBuilder();

        await foreach (var segment in processor.ProcessAsync(waveStream, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(segment.Text))
            {
                transcript.Append(segment.Text).Append(' ');
            }
        }

        return transcript.ToString().Trim();
    }

    private static MemoryStream CreateWavStream(float[] samples)
    {
        const int sampleRate = 16000;
        const short channels = 1;
        const short bitsPerSample = 16;
        int dataSize = samples.Length * 2;

        var stream = new MemoryStream(44 + dataSize);

        WriteAscii(stream, "RIFF");
        WriteInt32(stream, 36 + dataSize);
        WriteAscii(stream, "WAVE");
        WriteAscii(stream, "fmt ");
        WriteInt32(stream, 16);
        WriteInt16(stream, 1); // PCM
        WriteInt16(stream, channels);
        WriteInt32(stream, sampleRate);
        WriteInt32(stream, sampleRate * channels * bitsPerSample / 8);
        WriteInt16(stream, (short)(channels * bitsPerSample / 8));
        WriteInt16(stream, bitsPerSample);
        WriteAscii(stream, "data");
        WriteInt32(stream, dataSize);

        foreach (float sample in samples)
        {
            short pcm = (short)Math.Clamp(sample * 32767f, short.MinValue, short.MaxValue);
            WriteInt16(stream, pcm);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteAscii(MemoryStream stream, string value) =>
        stream.Write(System.Text.Encoding.ASCII.GetBytes(value));

    private static void WriteInt32(MemoryStream stream, int value) => stream.Write(BitConverter.GetBytes(value));

    private static void WriteInt16(MemoryStream stream, short value) => stream.Write(BitConverter.GetBytes(value));
}
