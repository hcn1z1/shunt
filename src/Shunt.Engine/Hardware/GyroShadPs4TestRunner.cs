using System.Collections.Concurrent;
using System.Diagnostics;

namespace Shunt.Engine.Hardware;

public sealed record GyroShadPs4TestProgress(TimeSpan Elapsed, TimeSpan Duration, ImuSensorSample LatestSample)
{
    public double FractionComplete => Duration <= TimeSpan.Zero ? 1d : Math.Clamp(Elapsed.TotalMilliseconds / Duration.TotalMilliseconds, 0d, 1d);
    public bool IsBaselineStage => Elapsed < TimeSpan.FromMilliseconds(300);
}

public sealed record GyroShadPs4TestResult(
    bool Passed,
    int PacketCount,
    double PollingRateHz,
    double AngularVelocityDeltaDegreesPerSecond,
    double GravityMagnitudeG,
    double NoiseFloorDegreesPerSecond,
    string Message);

/// <summary>Runs the three-second live motion check and evaluates the phase 3 verification criteria.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class GyroShadPs4TestRunner
{
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(3);
    public int MinimumPacketCount { get; init; } = 600;
    public double MinimumPollingRateHz { get; init; } = 200;
    public double MinimumAngularDeltaDegreesPerSecond { get; init; } = 120;
    public double MinimumGravityG { get; init; } = 0.75;
    public double MaximumGravityG { get; init; } = 1.25;
    public double MaximumNoiseDegreesPerSecond { get; init; } = 1.5;

    public async Task<GyroShadPs4TestResult> RunAsync(
        HardwareImuPoller poller,
        IProgress<GyroShadPs4TestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poller);
        if (poller.IsRunning) throw new InvalidOperationException("The IMU poller is already running.");

        var samples = new ConcurrentQueue<ImuSensorSample>();
        var clock = Stopwatch.StartNew();
        void OnSample(ImuSensorSample sample)
        {
            samples.Enqueue(sample);
            progress?.Report(new GyroShadPs4TestProgress(clock.Elapsed, SamplingDuration, sample));
        }

        poller.SampleReceived += OnSample;
        try
        {
            poller.Start();
            await Task.Delay(SamplingDuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await poller.StopAsync().ConfigureAwait(false);
            poller.SampleReceived -= OnSample;
        }

        return Evaluate(samples.ToArray(), SamplingDuration);
    }

    public GyroShadPs4TestResult Evaluate(IReadOnlyList<ImuSensorSample> samples, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var elapsedSeconds = Math.Max(duration.TotalSeconds, 0.001d);
        var rate = samples.Count / elapsedSeconds;
        if (samples.Count == 0)
            return new(false, 0, 0, 0, 0, double.PositiveInfinity, "No sensor reports were received. Check the connection and try again.");

        var minX = samples.Min(sample => sample.GyroX);
        var maxX = samples.Max(sample => sample.GyroX);
        var minY = samples.Min(sample => sample.GyroY);
        var maxY = samples.Max(sample => sample.GyroY);
        var minZ = samples.Min(sample => sample.GyroZ);
        var maxZ = samples.Max(sample => sample.GyroZ);
        var angularDelta = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        var gravity = samples.Average(sample => Math.Sqrt(
            sample.AccelX * sample.AccelX + sample.AccelY * sample.AccelY + sample.AccelZ * sample.AccelZ));

        // The wizard asks the user to keep the controller still for the first 300 ms,
        // then tilt it. Use that short baseline to estimate static sensor jitter.
        var baseline = samples.Where(sample => sample.TimestampMilliseconds - samples[0].TimestampMilliseconds <= 300).ToArray();
        var noise = EstimateNoiseFloor(baseline);
        var passed = samples.Count >= MinimumPacketCount &&
                     rate >= MinimumPollingRateHz &&
                     angularDelta >= MinimumAngularDeltaDegreesPerSecond &&
                     gravity >= MinimumGravityG && gravity <= MaximumGravityG &&
                     noise <= MaximumNoiseDegreesPerSecond;

        var message = passed
            ? "Hardware gyroscope verified for ShadPS4 emulation."
            : BuildFailureMessage(samples.Count, rate, angularDelta, gravity, noise);
        return new GyroShadPs4TestResult(passed, samples.Count, rate, angularDelta, gravity, noise, message);
    }

    private static double EstimateNoiseFloor(IReadOnlyList<ImuSensorSample> samples)
    {
        if (samples.Count < 2) return double.PositiveInfinity;
        return Math.Max(
            PeakDeviation(samples.Select(sample => sample.GyroX)),
            Math.Max(PeakDeviation(samples.Select(sample => sample.GyroY)), PeakDeviation(samples.Select(sample => sample.GyroZ))));
    }

    private static double PeakDeviation(IEnumerable<float> values)
    {
        var data = values.Select(value => (double)value).Order().ToArray();
        var median = data[data.Length / 2];
        return data.Max(value => Math.Abs(value - median));
    }

    private string BuildFailureMessage(int packets, double rate, double delta, double gravity, double noise)
    {
        if (packets < MinimumPacketCount || rate < MinimumPollingRateHz)
            return "The sensor stream is too slow or incomplete. Reconnect the controller and try again.";
        if (delta < MinimumAngularDeltaDegreesPerSecond)
            return "Not enough motion was detected. Keep the controller still briefly, then tilt it in all directions.";
        if (gravity < MinimumGravityG || gravity > MaximumGravityG)
            return "The accelerometer did not report a plausible gravity vector. Check the controller orientation and retry.";
        if (noise > MaximumNoiseDegreesPerSecond)
            return "The sensor noise is too high. Keep the controller still during the beginning of the test and retry.";
        return "The controller did not pass the motion sensor verification.";
    }
}
