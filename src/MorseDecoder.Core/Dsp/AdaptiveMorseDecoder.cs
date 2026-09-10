using System.Text;

namespace MorseDecoder.Core.Dsp;

internal sealed class AdaptiveMorseDecoder
{
    private readonly StringBuilder _currentCode = new(8);

    private bool _initialized;
    private bool _keyDown;
    private bool _lastSymbolWasSpace;
    private double _runMilliseconds;
    private double _unitMilliseconds = 60.0;
    private double _noiseFloor;
    private double _signalLevel;
    private double _lastConfidence;
    private double _idleMilliseconds;

    public double Wpm => 1200.0 / Math.Clamp(_unitMilliseconds, 20.0, 240.0);

    public double Confidence => _lastConfidence;

    public event Action<string, double, double>? Decoded;

    public void ProcessEnvelope(double envelope, double durationMilliseconds)
    {
        envelope = Math.Max(0.0, envelope);
        durationMilliseconds = Math.Max(0.01, durationMilliseconds);

        if (!_initialized)
        {
            _noiseFloor = envelope;
            _signalLevel = Math.Max(envelope * 1.5, 1e-5);
            _initialized = true;
        }

        if (!_keyDown)
        {
            _noiseFloor = (_noiseFloor * 0.995) + (Math.Min(envelope, _noiseFloor * 2.0) * 0.005);
        }

        _signalLevel = Math.Max(_signalLevel * 0.9998, envelope);
        _signalLevel = Math.Max(_signalLevel, _noiseFloor * 4.0);

        var range = Math.Max(_signalLevel - _noiseFloor, 1e-7);
        var normalized = Math.Clamp((envelope - _noiseFloor) / range, 0.0, 1.0);
        var nextState = _keyDown ? normalized > 0.28 : normalized > 0.55;

        if (nextState == _keyDown)
        {
            _runMilliseconds += durationMilliseconds;
            _idleMilliseconds = _keyDown ? 0.0 : _idleMilliseconds + durationMilliseconds;

            if (!_keyDown &&
                _currentCode.Length > 0 &&
                _runMilliseconds >= _unitMilliseconds * 5.0)
            {
                DecodeGap(_runMilliseconds);
            }

            return;
        }

        FinishRun(_keyDown, _runMilliseconds);
        _keyDown = nextState;
        _runMilliseconds = durationMilliseconds;
        _idleMilliseconds = nextState ? 0.0 : durationMilliseconds;
    }

    public void Reset()
    {
        _currentCode.Clear();
        _keyDown = false;
        _lastSymbolWasSpace = false;
        _runMilliseconds = 0.0;
        _unitMilliseconds = 60.0;
        _noiseFloor = 0.0;
        _signalLevel = 0.0;
        _initialized = false;
        _lastConfidence = 0.0;
        _idleMilliseconds = 0.0;
    }

    private void FinishRun(bool wasKeyDown, double durationMilliseconds)
    {
        if (durationMilliseconds <= 0.0)
        {
            return;
        }

        if (wasKeyDown)
        {
            DecodeElement(durationMilliseconds);
            return;
        }

        DecodeGap(durationMilliseconds);
    }

    private void DecodeElement(double durationMilliseconds)
    {
        if (durationMilliseconds < _unitMilliseconds * 0.25)
        {
            return;
        }

        var dotMean = _unitMilliseconds;
        var dashMean = _unitMilliseconds * 3.0;
        var dotSigma = Math.Max(_unitMilliseconds * 0.30, 4.0);
        var dashSigma = Math.Max(_unitMilliseconds * 0.45, 7.0);

        var dotScore = LogNormal(durationMilliseconds, dotMean, dotSigma);
        var dashScore = LogNormal(durationMilliseconds, dashMean, dashSigma);
        var isDot = dotScore >= dashScore;
        var difference = Math.Abs(dotScore - dashScore);

        _lastConfidence = 1.0 - Math.Exp(-difference);
        _currentCode.Append(isDot ? '.' : '-');

        if (durationMilliseconds >= _unitMilliseconds * 0.4 &&
            durationMilliseconds <= _unitMilliseconds * 6.0)
        {
            var observedUnit = isDot ? durationMilliseconds : durationMilliseconds / 3.0;
            var adaptation = isDot ? 0.08 : 0.05;
            _unitMilliseconds = Math.Clamp(
                (_unitMilliseconds * (1.0 - adaptation)) + (observedUnit * adaptation),
                20.0,
                240.0);
        }

        if (_currentCode.Length > 8)
        {
            Emit("?", _lastConfidence);
            _currentCode.Clear();
        }
    }

    private void DecodeGap(double durationMilliseconds)
    {
        if (_currentCode.Length == 0)
        {
            return;
        }

        if (durationMilliseconds < _unitMilliseconds * 1.7)
        {
            return;
        }

        if (durationMilliseconds < _unitMilliseconds * 5.0)
        {
            Emit(MorseCodec.Decode(_currentCode.ToString()), _lastConfidence);
        }
        else
        {
            Emit(MorseCodec.Decode(_currentCode.ToString()), _lastConfidence);
            Emit(" ", 0.8);
        }

        _currentCode.Clear();
    }

    private void Emit(string text, double confidence)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (text == " " && _lastSymbolWasSpace)
        {
            return;
        }

        _lastSymbolWasSpace = text == " ";
        Decoded?.Invoke(text, Math.Clamp(confidence, 0.0, 1.0), Wpm);
    }

    private static double LogNormal(double value, double mean, double sigma)
    {
        var delta = value - mean;
        return -(delta * delta) / (2.0 * sigma * sigma);
    }
}
