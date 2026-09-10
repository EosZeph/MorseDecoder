using MorseDecoder.Core.Dsp;
using MorseDecoder.Core.Synthetic;

const int sampleRate = 48_000;
const string expectedText = "CQ DE TEST";

await using var pipeline = new MorsePipeline(sampleRate);
pipeline.Start();

var signal = CwSignalGenerator.Generate(
    expectedText,
    sampleRate,
    toneFrequencyHz: 700.0,
    wpm: 20.0,
    amplitude: 0.22,
    noiseAmplitude: 0.012,
    seed: 20260910);

const int blockSize = 480;
for (var offset = 0; offset < signal.Length; offset += blockSize)
{
    var count = Math.Min(blockSize, signal.Length - offset);
    var block = signal.AsSpan(offset, count).ToArray();
    pipeline.Enqueue(block);
    await Task.Delay(1);
}

await Task.Delay(800);

var silence = new float[sampleRate / 2];
for (var offset = 0; offset < silence.Length; offset += blockSize)
{
    var count = Math.Min(blockSize, silence.Length - offset);
    pipeline.Enqueue(silence.AsSpan(offset, count).ToArray());
    await Task.Delay(1);
}

await Task.Delay(600);

var snapshots = pipeline.GetTrackSnapshots();
Console.WriteLine($"Tracks: {snapshots.Count}");

foreach (var snapshot in snapshots)
{
    Console.WriteLine(
        $"#{snapshot.Id} {snapshot.FrequencyHz:F1} Hz, " +
        $"{snapshot.SignalStrengthDb:F1} dB, {snapshot.Wpm:F1} WPM: " +
        $"{snapshot.Transcript}");
}

var transcript = string.Join(" ", snapshots
    .Select(snapshot => snapshot.Transcript)
    .Where(text => !string.IsNullOrWhiteSpace(text)));
var normalized = transcript.Replace("  ", " ", StringComparison.Ordinal).Trim();

if (!normalized.Contains(expectedText, StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Expected to find \"{expectedText}\", got \"{normalized}\".");
    return 1;
}

Console.WriteLine("Smoke test passed.");
return 0;
