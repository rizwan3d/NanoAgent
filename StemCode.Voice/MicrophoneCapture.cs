using System.Runtime.InteropServices;
using PortAudioSharp;

namespace StemCode.Voice;

/// <summary>
/// Captures microphone audio as 16 kHz mono float samples in the range [-1, 1],
/// suitable for Whisper.
///
/// Recording stops when:
/// - Speech has been detected and then continuous silence lasts long enough.
/// - The maximum recording duration is reached.
/// - Cancellation is requested.
///
/// Works on Windows, macOS, and Linux through PortAudio.
/// </summary>
internal static class MicrophoneCapture
{
    private const int SampleRate = 16_000;

    // Analyze audio in 20 ms windows.
    private const int AnalysisWindowMilliseconds = 20;
    private const int AnalysisWindowSamples =
        SampleRate * AnalysisWindowMilliseconds / 1000; // 320 samples

    // Anything below roughly -40 dBFS is considered silence.
    private const double SilenceThresholdDb = -40.0;

    // Stop after this much continuous silence once speech has started.
    private const int SilenceLimitMilliseconds = 1_200;
    private const int SilenceLimitSamples =
        SampleRate * SilenceLimitMilliseconds / 1000;

    private const int MaxDurationSeconds = 25;
    private const int MaxSampleCount = SampleRate * MaxDurationSeconds;

    public static async Task<float[]> CaptureAsync(
        int? deviceNumber,
        CancellationToken cancellationToken)
    {
        PortAudio.Initialize();

        try
        {
            int deviceIndex =
                deviceNumber ?? PortAudio.DefaultInputDevice;

            if (deviceIndex == PortAudio.NoDevice)
            {
                throw new InvalidOperationException(
                    "No microphone input device was found.");
            }

            DeviceInfo info = PortAudio.GetDeviceInfo(deviceIndex);

            if (info.maxInputChannels < 1)
            {
                throw new InvalidOperationException(
                    $"The selected device '{info.name}' does not support audio input.");
            }

            var parameters = new StreamParameters
            {
                device = deviceIndex,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = info.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            var samples = new List<float>(MaxSampleCount);

            var completion =
                new TaskCompletionSource<float[]>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var guard = new object();

            int silentSampleCount = 0;
            bool heardSpeech = false;
            bool completed = false;

            void ProcessAudio(float[] chunk)
            {
                lock (guard)
                {
                    if (completed)
                    {
                        return;
                    }

                    int remainingCapacity =
                        MaxSampleCount - samples.Count;

                    if (remainingCapacity <= 0)
                    {
                        return;
                    }

                    int samplesToKeep =
                        Math.Min(chunk.Length, remainingCapacity);

                    for (int i = 0; i < samplesToKeep; i++)
                    {
                        samples.Add(chunk[i]);
                    }

                    // Analyze the incoming chunk in small fixed windows.
                    int offset = 0;

                    while (offset < samplesToKeep)
                    {
                        int windowLength = Math.Min(
                            AnalysisWindowSamples,
                            samplesToKeep - offset);

                        double rms =
                            ComputeRms(chunk, offset, windowLength);

                        double db = RmsToDb(rms);

                        if (db < SilenceThresholdDb)
                        {
                            if (heardSpeech)
                            {
                                silentSampleCount += windowLength;
                            }
                        }
                        else
                        {
                            heardSpeech = true;
                            silentSampleCount = 0;
                        }

                        offset += windowLength;
                    }
                }
            }

            PortAudioSharp.Stream.Callback callback =
                (
                    IntPtr input,
                    IntPtr output,
                    uint frameCount,
                    ref StreamCallbackTimeInfo timeInfo,
                    StreamCallbackFlags statusFlags,
                    IntPtr userDataPtr) =>
                {
                    if (input == IntPtr.Zero || frameCount == 0)
                    {
                        return StreamCallbackResult.Continue;
                    }

                    int count = checked((int)frameCount);

                    var chunk = new float[count];

                    Marshal.Copy(
                        input,
                        chunk,
                        0,
                        count);

                    ProcessAudio(chunk);

                    return StreamCallbackResult.Continue;
                };

            using var stream = new PortAudioSharp.Stream(
                inParams: parameters,
                outParams: null,
                sampleRate: SampleRate,
                framesPerBuffer: 0,
                streamFlags: StreamFlags.ClipOff,
                callback: callback,
                userData: null);

            try
            {
                stream.Start();
            }
            catch (PortAudioException exception)
            {
                throw new InvalidOperationException(
                    $"Unable to start microphone capture on device '{info.name}'.",
                    exception);
            }

            void StopCapture()
            {
                float[] capturedSamples;

                lock (guard)
                {
                    if (completed)
                    {
                        return;
                    }

                    completed = true;
                    capturedSamples = samples.ToArray();
                }

                try
                {
                    stream.Stop();
                }
                catch (PortAudioException)
                {
                    // Stream may already be stopping or stopped.
                    // The captured samples are still usable.
                }

                completion.TrySetResult(capturedSamples);
            }

            // Monitor state outside of the real-time PortAudio callback.
            //
            // We intentionally don't call stream.Stop() directly inside the
            // PortAudio callback because stopping/closing a stream from its
            // callback can cause backend-specific problems.
            Task monitor = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Task.Delay(
                            50,
                            cancellationToken);

                        int sampleCount;
                        int silenceSamples;
                        bool speechDetected;
                        bool alreadyCompleted;

                        lock (guard)
                        {
                            sampleCount = samples.Count;
                            silenceSamples = silentSampleCount;
                            speechDetected = heardSpeech;
                            alreadyCompleted = completed;
                        }

                        if (alreadyCompleted)
                        {
                            return;
                        }

                        bool silenceReached =
                            speechDetected &&
                            silenceSamples >= SilenceLimitSamples;

                        bool maximumReached =
                            sampleCount >= MaxSampleCount;

                        if (silenceReached || maximumReached)
                        {
                            StopCapture();
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    StopCapture();
                }
            });

            using CancellationTokenRegistration registration =
                cancellationToken.Register(StopCapture);

            float[] result = await completion.Task;

            await monitor;

            return result;
        }
        finally
        {
            PortAudio.Terminate();
        }
    }

    /// <summary>
    /// Calculates RMS for a section of a float sample buffer.
    /// </summary>
    private static double ComputeRms(
        float[] samples,
        int offset,
        int count)
    {
        if (count <= 0)
        {
            return 0d;
        }

        double sumOfSquares = 0d;

        int end = offset + count;

        for (int i = offset; i < end; i++)
        {
            double value = samples[i];
            sumOfSquares += value * value;
        }

        return Math.Sqrt(sumOfSquares / count);
    }

    /// <summary>
    /// Converts RMS amplitude to dBFS.
    /// </summary>
    private static double RmsToDb(double rms)
    {
        if (rms <= 0d)
        {
            return double.NegativeInfinity;
        }

        return 20d * Math.Log10(rms);
    }
}
