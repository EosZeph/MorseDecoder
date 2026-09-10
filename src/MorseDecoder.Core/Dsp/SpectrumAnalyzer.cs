namespace MorseDecoder.Core.Dsp;

internal sealed class SpectrumAnalyzer
{
    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly float[] _window;
    private readonly float[] _real;
    private readonly float[] _imaginary;
    private readonly float[] _dbBins;
    private readonly double _windowSum;

    public SpectrumAnalyzer(int sampleRate, int fftSize)
    {
        if ((fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two.", nameof(fftSize));
        }

        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _window = new float[fftSize];
        _real = new float[fftSize];
        _imaginary = new float[fftSize];
        _dbBins = new float[(fftSize / 2) + 1];

        for (var index = 0; index < fftSize; index++)
        {
            _window[index] = 0.5f - (0.5f * MathF.Cos(2f * MathF.PI * index / (fftSize - 1)));
            _windowSum += _window[index];
        }
    }

    public int BinCount => _dbBins.Length;

    public double BinWidthHz => (double)_sampleRate / _fftSize;

    public float[] Analyze(ReadOnlySpan<float> samples)
    {
        if (samples.Length < _fftSize)
        {
            throw new ArgumentException("Not enough samples for one FFT frame.", nameof(samples));
        }

        for (var index = 0; index < _fftSize; index++)
        {
            _real[index] = samples[index] * _window[index];
            _imaginary[index] = 0f;
        }

        Fft.Forward(_real, _imaginary);

        for (var index = 0; index < _dbBins.Length; index++)
        {
            var power = (_real[index] * _real[index]) + (_imaginary[index] * _imaginary[index]);
            var amplitude = 2.0 * Math.Sqrt(power) / _windowSum;
            _dbBins[index] = (float)(20.0 * Math.Log10(Math.Max(amplitude, 1e-7)));
        }

        return _dbBins;
    }
}

