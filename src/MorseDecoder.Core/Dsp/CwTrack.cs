using System.Text;

namespace MorseDecoder.Core.Dsp;

public sealed record TrackSnapshot(
    int Id,
    double FrequencyHz,
    double SignalStrengthDb,
    double Wpm,
    double Confidence,
    bool IsActive,
    string Transcript);

internal sealed class CwTrack
{
    private readonly object _sync = new();
    private readonly int _sampleRate;
    private readonly AdaptiveMorseDecoder _decoder = new();
    private readonly StringBuilder _transcript = new();
    private readonly int _samplesPerEnvelopeUpdate;
    private readonly double _mixPhaseStep;
    private readonly double _filterAlpha;

    private double _mixPhase;
    private double _basebandI;
    private double _basebandQ;
    private double _envelopeAccumulator;
    private int _envelopeSampleCount;
    private double _frequencyHz;
    private double _strengthDb;
    private double _confidence;

    public CwTrack(int id, int sampleRate, double frequencyHz)
    {
        Id = id;
        _sampleRate = sampleRate;
        _frequencyHz = frequencyHz;
        _mixPhaseStep = 2.0 * Math.PI * frequencyHz / sampleRate;
        _samplesPerEnvelopeUpdate = Math.Max(1, sampleRate / 1000);

        const double filterCutoffHz = 100.0;
        _filterAlpha = 1.0 - Math.Exp(-2.0 * Math.PI * filterCutoffHz / sampleRate);
        _decoder.Decoded += OnDecoded;
    }

    public int Id { get; }

    public bool IsActive { get; private set; } = true;

    public double FrequencyHz
    {
        get
        {
            lock (_sync)
            {
                return _frequencyHz;
            }
        }
    }

    public void ProcessSamples(ReadOnlySpan<float> samples)
    {
        if (!IsActive)
        {
            return;
        }

        lock (_sync)
        {
            foreach (var sample in samples)
            {
                var phase = _mixPhase;
                var mixedI = sample * Math.Cos(phase);
                var mixedQ = -sample * Math.Sin(phase);

                _basebandI += _filterAlpha * (mixedI - _basebandI);
                _basebandQ += _filterAlpha * (mixedQ - _basebandQ);

                var envelope = Math.Sqrt((_basebandI * _basebandI) + (_basebandQ * _basebandQ));
                _envelopeAccumulator += envelope;
                _envelopeSampleCount++;

                if (_envelopeSampleCount >= _samplesPerEnvelopeUpdate)
                {
                    var averageEnvelope = _envelopeAccumulator / _envelopeSampleCount;
                    _decoder.ProcessEnvelope(averageEnvelope, 1.0);
                    _envelopeAccumulator = 0.0;
                    _envelopeSampleCount = 0;
                }

                _mixPhase += _mixPhaseStep;
                if (_mixPhase >= 2.0 * Math.PI)
                {
                    _mixPhase -= 2.0 * Math.PI;
                }
            }
        }
    }

    public void UpdateDetection(double frequencyHz, double strengthDb)
    {
        lock (_sync)
        {
            _frequencyHz = (_frequencyHz * 0.88) + (frequencyHz * 0.12);
            _strengthDb = (_strengthDb * 0.75) + (strengthDb * 0.25);
            _confidence = _decoder.Confidence;
        }
    }

    public void MarkInactive()
    {
        IsActive = false;
    }

    public void MarkActive()
    {
        if (IsActive)
        {
            return;
        }

        lock (_sync)
        {
            _basebandI = 0.0;
            _basebandQ = 0.0;
            _envelopeAccumulator = 0.0;
            _envelopeSampleCount = 0;
            _mixPhase = 0.0;
            IsActive = true;
        }
    }

    public TrackSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new TrackSnapshot(
                Id,
                FrequencyHz,
                _strengthDb,
                _decoder.Wpm,
                _confidence,
                IsActive,
                _transcript.ToString());
        }
    }

    private void OnDecoded(string text, double confidence, double wpm)
    {
        lock (_sync)
        {
            _confidence = confidence;
            _transcript.Append(text);

            const int maxTranscriptLength = 12_000;
            if (_transcript.Length > maxTranscriptLength)
            {
                _transcript.Remove(0, _transcript.Length - maxTranscriptLength);
            }
        }
    }
}
