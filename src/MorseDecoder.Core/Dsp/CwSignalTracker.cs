namespace MorseDecoder.Core.Dsp;

internal sealed class CwSignalTracker
{
    private const int MaxTracks = 32;
    private const int ConfirmationFrames = 3;
    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly double _binWidthHz;
    private readonly List<TrackState> _tracks = [];
    private readonly List<Peak> _peaks = [];
    private readonly List<float> _noiseSamples = [];
    private int _nextTrackId = 1;
    private double _lastFrameTimeMs;

    public CwSignalTracker(int sampleRate, int fftSize, double frameTimeMs)
    {
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _binWidthHz = (double)sampleRate / fftSize;
        _lastFrameTimeMs = frameTimeMs;
    }

    public IReadOnlyList<CwTrack> ActiveTracks
    {
        get
        {
            lock (_tracks)
            {
                return _tracks.Select(track => track.Track).ToArray();
            }
        }
    }

    public void Update(ReadOnlySpan<float> dbBins, double frameTimeMs)
    {
        _lastFrameTimeMs = Math.Max(1.0, frameTimeMs);

        lock (_tracks)
        {
            DetectPeaks(dbBins);
            MatchTracks();
            CreateConfirmedTracks();
            RemoveExpiredTracks();
        }
    }

    public IReadOnlyList<TrackSnapshot> GetSnapshots()
    {
        lock (_tracks)
        {
            return _tracks
                .Where(state => state.Confirmed)
                .Select(state => state.Track.GetSnapshot())
                .OrderByDescending(snapshot => snapshot.SignalStrengthDb)
                .ToArray();
        }
    }

    public void Clear()
    {
        lock (_tracks)
        {
            _tracks.Clear();
            _peaks.Clear();
            _nextTrackId = 1;
        }
    }

    private void DetectPeaks(ReadOnlySpan<float> dbBins)
    {
        _peaks.Clear();
        _noiseSamples.Clear();

        var minimumBin = Math.Max(1, (int)Math.Ceiling(80.0 / _binWidthHz));
        var maximumBin = Math.Min(dbBins.Length - 2, (int)Math.Floor(3900.0 / _binWidthHz));
        if (maximumBin <= minimumBin)
        {
            return;
        }

        for (var index = minimumBin; index <= maximumBin; index++)
        {
            _noiseSamples.Add(dbBins[index]);
        }

        _noiseSamples.Sort();
        var noiseIndex = Math.Clamp((int)(_noiseSamples.Count * 0.50), 0, _noiseSamples.Count - 1);
        var noiseFloor = _noiseSamples[noiseIndex];
        var threshold = noiseFloor + 9.0;

        for (var index = minimumBin; index <= maximumBin; index++)
        {
            var value = dbBins[index];
            if (value < threshold ||
                value <= dbBins[index - 1] ||
                value < dbBins[index + 1])
            {
                continue;
            }

            var frequency = index * _binWidthHz;
            _peaks.Add(new Peak(frequency, value));
        }

        _peaks.Sort((left, right) => right.StrengthDb.CompareTo(left.StrengthDb));
        if (_peaks.Count > MaxTracks * 2)
        {
            _peaks.RemoveRange(MaxTracks * 2, _peaks.Count - (MaxTracks * 2));
        }
    }

    private void MatchTracks()
    {
        var matchedPeaks = new bool[_peaks.Count];
        var matchDistance = Math.Max(18.0, _binWidthHz * 1.5);

        foreach (var state in _tracks)
        {
            var bestIndex = -1;
            var bestDistance = double.MaxValue;

            for (var index = 0; index < _peaks.Count; index++)
            {
                if (matchedPeaks[index])
                {
                    continue;
                }

                var distance = Math.Abs(_peaks[index].FrequencyHz - state.Track.FrequencyHz);
                if (distance < bestDistance && distance <= matchDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
            {
                state.Misses++;
                state.ConsecutiveHits = 0;
                if (state.Misses * _lastFrameTimeMs >= 700.0)
                {
                    state.Track.MarkInactive();
                }

                continue;
            }

            matchedPeaks[bestIndex] = true;
            state.Misses = 0;
            state.Hits++;
            state.ConsecutiveHits++;
            state.Track.UpdateDetection(_peaks[bestIndex].FrequencyHz, _peaks[bestIndex].StrengthDb);
            state.Track.MarkActive();

            if (!state.Confirmed && state.ConsecutiveHits >= ConfirmationFrames)
            {
                state.Confirmed = true;
            }
        }

        for (var index = 0; index < _peaks.Count; index++)
        {
            if (matchedPeaks[index] || _tracks.Count >= MaxTracks)
            {
                continue;
            }

            var peak = _peaks[index];
            var existing = _tracks.Any(state =>
                Math.Abs(state.Track.FrequencyHz - peak.FrequencyHz) < Math.Max(18.0, _binWidthHz * 1.5));

            if (!existing)
            {
                _tracks.Add(new TrackState(
                    new CwTrack(_nextTrackId++, _sampleRate, peak.FrequencyHz),
                    peak.StrengthDb));
            }
        }
    }

    private void CreateConfirmedTracks()
    {
        // Confirmation happens during matching, so this method intentionally keeps
        // the update sequence explicit for later Bayesian track confidence work.
    }

    private void RemoveExpiredTracks()
    {
        _tracks.RemoveAll(state =>
            state.Misses * _lastFrameTimeMs >= 6000.0 ||
            (!state.Confirmed && state.Misses >= 4));
    }

    private readonly record struct Peak(double FrequencyHz, double StrengthDb);

    private sealed class TrackState(CwTrack track, double initialStrengthDb)
    {
        public CwTrack Track { get; } = track;
        public int Hits { get; set; }
        public int ConsecutiveHits { get; set; }
        public int Misses { get; set; }
        public bool Confirmed { get; set; }
        public double InitialStrengthDb { get; } = initialStrengthDb;
    }
}
