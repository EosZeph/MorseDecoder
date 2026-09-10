using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MorseDecoder.Core.Dsp;

public sealed class MorsePipeline : IAsyncDisposable
{
    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly int _hopSize;
    private readonly SpectrumAnalyzer _spectrumAnalyzer;
    private readonly CwSignalTracker _tracker;
    private readonly Channel<float[]> _input = Channel.CreateBounded<float[]>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentQueue<float[]> _waterfallFrames = new();
    private readonly float[] _analysisBuffer;
    private readonly CancellationTokenSource _cancellation = new();

    private Task? _processingTask;
    private int _analysisCount;
    private long _samplePosition;
    private int _droppedBlocks;
    private bool _disposed;

    public MorsePipeline(int sampleRate = 48_000, int fftSize = 1024, int hopSize = 256)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (hopSize <= 0 || hopSize >= fftSize)
        {
            throw new ArgumentOutOfRangeException(nameof(hopSize));
        }

        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _hopSize = hopSize;
        _analysisBuffer = new float[fftSize * 4];
        _spectrumAnalyzer = new SpectrumAnalyzer(sampleRate, fftSize);

        var frameTimeMs = hopSize * 1000.0 / sampleRate;
        _tracker = new CwSignalTracker(sampleRate, fftSize, frameTimeMs);
    }

    public int DroppedBlocks => _droppedBlocks;

    public void Start()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MorsePipeline));
        }

        if (_processingTask is not null)
        {
            return;
        }

        _processingTask = Task.Run(() => ProcessLoopAsync(_cancellation.Token));
    }

    public bool Enqueue(float[] samples)
    {
        if (samples.Length == 0)
        {
            return true;
        }

        if (_input.Writer.TryWrite(samples))
        {
            return true;
        }

        Interlocked.Increment(ref _droppedBlocks);
        return false;
    }

    public IReadOnlyList<TrackSnapshot> GetTrackSnapshots()
    {
        return _tracker.GetSnapshots();
    }

    public bool TryDequeueWaterfallFrame(out float[]? frame)
    {
        return _waterfallFrames.TryDequeue(out frame);
    }

    public void Reset()
    {
        _tracker.Clear();
        _waterfallFrames.Clear();
        _analysisCount = 0;
        _samplePosition = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _input.Writer.TryComplete();

        if (_processingTask is not null)
        {
            try
            {
                await _processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var block in _input.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            ProcessBlock(block);
        }
    }

    private void ProcessBlock(float[] samples)
    {
        foreach (var track in _tracker.ActiveTracks)
        {
            track.ProcessSamples(samples);
        }

        FeedSpectrum(samples);
    }

    private void FeedSpectrum(ReadOnlySpan<float> samples)
    {
        var offset = 0;
        while (offset < samples.Length)
        {
            var available = Math.Min(_analysisBuffer.Length - _analysisCount, samples.Length - offset);
            samples.Slice(offset, available).CopyTo(_analysisBuffer.AsSpan(_analysisCount));
            _analysisCount += available;
            offset += available;

            while (_analysisCount >= _fftSize)
            {
                var dbBins = _spectrumAnalyzer.Analyze(_analysisBuffer.AsSpan(0, _fftSize));
                var frameTimeMs = _hopSize * 1000.0 / _sampleRate;
                _tracker.Update(dbBins, frameTimeMs);

                var frame = new float[dbBins.Length];
                dbBins.CopyTo(frame, 0);
                _waterfallFrames.Enqueue(frame);

                while (_waterfallFrames.Count > 1200 && _waterfallFrames.TryDequeue(out _))
                {
                }

                Array.Copy(_analysisBuffer, _hopSize, _analysisBuffer, 0, _analysisCount - _hopSize);
                _analysisCount -= _hopSize;
                _samplePosition += _hopSize;
            }
        }
    }
}
