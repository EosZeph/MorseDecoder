using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MorseDecoder.App.Rendering;

internal sealed class WaterfallBitmap
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _minimumBin;
    private readonly int _maximumBin;
    private readonly float[] _history;
    private readonly byte[] _pixels;
    private readonly int _stride;
    private int _writeColumn;
    private bool _dirty = true;

    public WaterfallBitmap(int width, int height, int minimumBin, int maximumBin)
    {
        _width = width;
        _height = height;
        _minimumBin = minimumBin;
        _maximumBin = maximumBin;
        _history = new float[width * height];
        _stride = width * 4;
        _pixels = new byte[_stride * height];
        Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        Render();
    }

    public WriteableBitmap Bitmap { get; }

    public void Append(ReadOnlySpan<float> dbBins)
    {
        if (dbBins.Length <= _maximumBin)
        {
            return;
        }

        var sourceSpan = Math.Max(1, _maximumBin - _minimumBin);
        var columnOffset = _writeColumn * _height;

        for (var y = 0; y < _height; y++)
        {
            var normalizedY = (_height - 1 - y) / (double)Math.Max(1, _height - 1);
            var sourcePosition = _minimumBin + (normalizedY * sourceSpan);
            var sourceIndex = (int)sourcePosition;
            var fraction = sourcePosition - sourceIndex;

            var first = dbBins[Math.Clamp(sourceIndex, 0, dbBins.Length - 1)];
            var second = dbBins[Math.Clamp(sourceIndex + 1, 0, dbBins.Length - 1)];
            _history[columnOffset + y] = (float)((first * (1.0 - fraction)) + (second * fraction));
        }

        _writeColumn = (_writeColumn + 1) % _width;
        _dirty = true;
    }

    public void Render()
    {
        if (!_dirty)
        {
            return;
        }

        for (var x = 0; x < _width; x++)
        {
            var sourceColumn = (_writeColumn + x) % _width;
            var sourceOffset = sourceColumn * _height;

            for (var y = 0; y < _height; y++)
            {
                var db = _history[sourceOffset + y];
                var normalized = Math.Clamp((db + 105.0f) / 90.0f, 0.0f, 1.0f);
                var value = normalized * normalized;
                var color = MapColor(value);
                var pixelOffset = (y * _width + x) * 4;

                _pixels[pixelOffset] = color.B;
                _pixels[pixelOffset + 1] = color.G;
                _pixels[pixelOffset + 2] = color.R;
                _pixels[pixelOffset + 3] = 255;
            }
        }

        Bitmap.WritePixels(
            new Int32Rect(0, 0, _width, _height),
            _pixels,
            _stride,
            0);

        _dirty = false;
    }

    public void Clear()
    {
        Array.Clear(_history, 0, _history.Length);
        Array.Clear(_pixels, 0, _pixels.Length);
        _writeColumn = 0;
        _dirty = true;
        Render();
    }

    private static Color MapColor(float value)
    {
        if (value < 0.24f)
        {
            return Interpolate(
                Color.FromRgb(4, 10, 18),
                Color.FromRgb(13, 52, 91),
                value / 0.24f);
        }

        if (value < 0.53f)
        {
            return Interpolate(
                Color.FromRgb(13, 52, 91),
                Color.FromRgb(0, 188, 212),
                (value - 0.24f) / 0.29f);
        }

        if (value < 0.82f)
        {
            return Interpolate(
                Color.FromRgb(0, 188, 212),
                Color.FromRgb(250, 204, 21),
                (value - 0.53f) / 0.29f);
        }

        return Interpolate(
            Color.FromRgb(250, 204, 21),
            Color.FromRgb(255, 248, 220),
            (value - 0.82f) / 0.18f);
    }

    private static Color Interpolate(Color start, Color end, float amount)
    {
        amount = Math.Clamp(amount, 0.0f, 1.0f);
        return Color.FromRgb(
            (byte)(start.R + ((end.R - start.R) * amount)),
            (byte)(start.G + ((end.G - start.G) * amount)),
            (byte)(start.B + ((end.B - start.B) * amount)));
    }
}
