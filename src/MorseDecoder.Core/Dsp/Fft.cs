using System.Numerics;

namespace MorseDecoder.Core.Dsp;

internal static class Fft
{
    public static void Forward(float[] real, float[] imaginary)
    {
        if (real.Length != imaginary.Length || !IsPowerOfTwo(real.Length))
        {
            throw new ArgumentException("FFT arrays must have the same power-of-two length.");
        }

        var length = real.Length;
        var levels = BitOperations.Log2((uint)length);

        for (var index = 0; index < length; index++)
        {
            var reversed = ReverseBits(index, levels);
            if (reversed <= index)
            {
                continue;
            }

            (real[index], real[reversed]) = (real[reversed], real[index]);
            (imaginary[index], imaginary[reversed]) = (imaginary[reversed], imaginary[index]);
        }

        for (var size = 2; size <= length; size <<= 1)
        {
            var halfSize = size >> 1;
            var angleStep = -2.0 * Math.PI / size;

            for (var start = 0; start < length; start += size)
            {
                for (var offset = 0; offset < halfSize; offset++)
                {
                    var angle = angleStep * offset;
                    var cosine = Math.Cos(angle);
                    var sine = Math.Sin(angle);
                    var evenIndex = start + offset;
                    var oddIndex = evenIndex + halfSize;

                    var oddReal = real[oddIndex] * (float)cosine - imaginary[oddIndex] * (float)sine;
                    var oddImaginary = real[oddIndex] * (float)sine + imaginary[oddIndex] * (float)cosine;

                    real[oddIndex] = real[evenIndex] - oddReal;
                    imaginary[oddIndex] = imaginary[evenIndex] - oddImaginary;
                    real[evenIndex] += oddReal;
                    imaginary[evenIndex] += oddImaginary;
                }
            }
        }
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    private static int ReverseBits(int value, int bitCount)
    {
        var reversed = 0;
        for (var index = 0; index < bitCount; index++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }

        return reversed;
    }
}
