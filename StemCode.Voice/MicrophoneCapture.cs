using NAudio.Wave;
using System.Diagnostics;

namespace StemCode.Voice;

/// <summary>
/// Captures microphone audio as 16 kHz mono 16-bit PCM and returns it as
/// normalized float samples suitable for Whisper. Stops automatically after a
/// period of silence or when the maximum duration is reached, and honors
/// cancellation (for example Ctrl+C) by returning whatever was captured.
/// </summary>
internal static class MicrophoneCapture
{
    private const double SilenceThreshold = 0.01;
    private const int SilenceLimitMilliseconds = 1200;
    private const int SampleRate = 16000;
    private const int MaxDurationSeconds = 25;

    public static async Task<float[]> CaptureAsync(
        int? deviceNumber,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Microphone capture for voice dictation is currently supported on Windows only.");
        }

        var samples = new List<float>(SampleRate * MaxDurationSeconds);
        var completion = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber ?? 0,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50
        };

        var stopwatch = Stopwatch.StartNew();
        var silentMilliseconds = 0;
        var heardSpeech = false;

        waveIn.DataAvailable += (_, args) =>
        {
            for (int offset = 0; offset + 1 < args.BytesRecorded; offset += 2)
            {
                short value = BitConverter.ToInt16(args.Buffer, offset);
                samples.Add(value / 32768f);
            }

            double rms = ComputeRms(args.Buffer, args.BytesRecorded);
            if (rms < SilenceThreshold)
            {
                silentMilliseconds += waveIn.BufferMilliseconds;
            }
            else
            {
                heardSpeech = true;
                silentMilliseconds = 0;
            }

            if ((heardSpeech && silentMilliseconds >= SilenceLimitMilliseconds) ||
                stopwatch.Elapsed.TotalSeconds >= MaxDurationSeconds)
            {
                waveIn.StopRecording();
            }
        };

        waveIn.RecordingStopped += (_, _) => completion.TrySetResult(samples.ToArray());

        using (cancellationToken.Register(() => waveIn.StopRecording()))
        {
            waveIn.StartRecording();
            float[] captured = await completion.Task;
            stopwatch.Stop();
            return captured;
        }
    }

    private static double ComputeRms(byte[] buffer, int bytesRecorded)
    {
        long sumOfSquares = 0;
        int count = 0;

        for (int offset = 0; offset + 1 < bytesRecorded; offset += 2)
        {
            short value = BitConverter.ToInt16(buffer, offset);
            sumOfSquares += (long)value * value;
            count++;
        }

        return count == 0 ? 0 : Math.Sqrt(sumOfSquares / (double)count) / 32768.0;
    }
}
