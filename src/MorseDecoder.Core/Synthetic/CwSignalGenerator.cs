using MorseDecoder.Core.Dsp;

namespace MorseDecoder.Core.Synthetic;

public static class CwSignalGenerator
{
    public static float[] Generate(
        string text,
        int sampleRate = 48_000,
        double toneFrequencyHz = 700.0,
        double wpm = 20.0,
        double amplitude = 0.22,
        double noiseAmplitude = 0.01,
        int seed = 12345)
    {
        var random = new Random(seed);
        var unitMilliseconds = 1200.0 / Math.Clamp(wpm, 5.0, 60.0);
        var timeline = BuildTimeline(text, unitMilliseconds);
        var totalMilliseconds = timeline.Sum(segment => segment.DurationMilliseconds) + (2 * unitMilliseconds);
        var sampleCount = (int)Math.Ceiling(totalMilliseconds * sampleRate / 1000.0);
        var samples = new float[sampleCount];
        var phase = 0.0;
        var phaseStep = 2.0 * Math.PI * toneFrequencyHz / sampleRate;
        var rampMilliseconds = Math.Max(3.0, unitMilliseconds * 0.08);

        var cursor = (int)Math.Ceiling(unitMilliseconds * sampleRate / 1000.0);
        foreach (var segment in timeline)
        {
            var segmentSamples = (int)Math.Round(segment.DurationMilliseconds * sampleRate / 1000.0);
            var rampSamples = Math.Max(1, (int)Math.Round(rampMilliseconds * sampleRate / 1000.0));

            for (var index = 0; index < segmentSamples && cursor < samples.Length; index++, cursor++)
            {
                var noise = (random.NextDouble() * 2.0 - 1.0) * noiseAmplitude;
                if (!segment.IsOn)
                {
                    samples[cursor] = (float)noise;
                    continue;
                }

                var ramp = Math.Min(1.0, Math.Min(index + 1, segmentSamples - index) / (double)rampSamples);
                samples[cursor] = (float)((Math.Sin(phase) * amplitude * ramp) + noise);
                phase += phaseStep;
                if (phase >= 2.0 * Math.PI)
                {
                    phase -= 2.0 * Math.PI;
                }
            }
        }

        return samples;
    }

    private static List<TimelineSegment> BuildTimeline(string text, double unitMilliseconds)
    {
        var timeline = new List<TimelineSegment>();

        for (var textIndex = 0; textIndex < text.Length; textIndex++)
        {
            var character = text[textIndex];
            if (character == ' ')
            {
                timeline.Add(new TimelineSegment(false, unitMilliseconds * 7.0));
                continue;
            }

            if (!MorseCodec.TryEncode(character, out var code))
            {
                continue;
            }

            for (var codeIndex = 0; codeIndex < code.Length; codeIndex++)
            {
                var symbol = code[codeIndex];
                timeline.Add(new TimelineSegment(true, symbol == '-' ? unitMilliseconds * 3.0 : unitMilliseconds));

                if (codeIndex < code.Length - 1)
                {
                    timeline.Add(new TimelineSegment(false, unitMilliseconds));
                }
            }

            if (textIndex < text.Length - 1 && text[textIndex + 1] != ' ')
            {
                timeline.Add(new TimelineSegment(false, unitMilliseconds * 3.0));
            }
        }

        timeline.Add(new TimelineSegment(false, unitMilliseconds * 7.0));
        return timeline;
    }

    private readonly record struct TimelineSegment(bool IsOn, double DurationMilliseconds);
}
