namespace MorseDecoder.Core.Synthetic;

public sealed class SyntheticCwSource : IDisposable
{
    private readonly Action<float[]> _sink;
    private readonly int _sampleRate;
    private readonly int _blockSamples;
    private readonly float[] _signal;
    private Timer? _timer;
    private int _position;
    private bool _disposed;

    public SyntheticCwSource(Action<float[]> sink, int sampleRate = 48_000, int blockMilliseconds = 20)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _sampleRate = sampleRate;
        _blockSamples = Math.Max(1, sampleRate * blockMilliseconds / 1000);
        _signal = CwSignalGenerator.Generate(
            "CQ DE MORSE TEST ",
            sampleRate,
            toneFrequencyHz: 700.0,
            wpm: 20.0,
            amplitude: 0.24,
            noiseAmplitude: 0.012,
            seed: 7319);
    }

    public void Start()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SyntheticCwSource));
        }

        _timer ??= new Timer(OnTimer, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void OnTimer(object? state)
    {
        var block = new float[_blockSamples];

        for (var index = 0; index < block.Length; index++)
        {
            block[index] = _signal[_position];
            _position++;
            if (_position >= _signal.Length)
            {
                _position = 0;
            }
        }

        _sink(block);
    }
}
