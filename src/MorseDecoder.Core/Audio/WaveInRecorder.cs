using System.Runtime.InteropServices;

namespace MorseDecoder.Core.Audio;

public sealed record AudioInputDevice(int DeviceId, string Name, int Channels);

public sealed class AudioSamplesEventArgs(float[] samples) : EventArgs
{
    public float[] Samples { get; } = samples;
}

public sealed class WaveInRecorder : IDisposable
{
    private const int MmsyserrNoError = 0;
    private const int WaveMapper = -1;
    private const int WaveFormatPcm = 1;
    private const int CallbackFunction = 0x00030000;

    private const int MmWimOpen = 0x3BE;
    private const int MmWimClose = 0x3BF;
    private const int MmWimData = 0x3C0;

    private readonly int _sampleRate;
    private readonly int _bufferMilliseconds;
    private readonly int _bufferCount;
    private readonly WaveInProc _callback;
    private readonly BufferSlot[] _slots;
    private readonly object _sync = new();

    private IntPtr _handle;
    private GCHandle _selfHandle;
    private bool _disposed;
    private volatile bool _stopping;

    public WaveInRecorder(int sampleRate = 48_000, int bufferMilliseconds = 20, int bufferCount = 5)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (bufferMilliseconds < 10 || bufferMilliseconds > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferMilliseconds));
        }

        if (bufferCount < 2 || bufferCount > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferCount));
        }

        _sampleRate = sampleRate;
        _bufferMilliseconds = bufferMilliseconds;
        _bufferCount = bufferCount;
        _callback = OnWaveInCallback;
        _slots = new BufferSlot[bufferCount];
    }

    public event EventHandler<AudioSamplesEventArgs>? DataAvailable;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _handle != IntPtr.Zero;
            }
        }
    }

    public static IReadOnlyList<AudioInputDevice> GetDevices()
    {
        var count = (int)waveInGetNumDevs();
        var devices = new List<AudioInputDevice>(count);

        for (var index = 0; index < count; index++)
        {
            var result = waveInGetDevCaps(new IntPtr(index), out var caps, Marshal.SizeOf<WaveInCaps>());
            if (result != MmsyserrNoError)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(caps.ProductName)
                ? $"输入设备 {index + 1}"
                : caps.ProductName.Trim();

            devices.Add(new AudioInputDevice(index, name, caps.Channels));
        }

        return devices;
    }

    public void Start(int deviceId)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WaveInRecorder));
        }

        lock (_sync)
        {
            if (_handle != IntPtr.Zero)
            {
                throw new InvalidOperationException("Audio capture is already running.");
            }

            var format = new WaveFormatEx
            {
                FormatTag = WaveFormatPcm,
                Channels = (ushort)1,
                SamplesPerSecond = (uint)_sampleRate,
                BitsPerSample = (ushort)16,
                BlockAlign = (ushort)2,
                AverageBytesPerSecond = (uint)(_sampleRate * 2),
                ExtraSize = 0
            };

            _selfHandle = GCHandle.Alloc(this);
            _stopping = false;

            var result = waveInOpen(
                out _handle,
                new IntPtr(deviceId),
                ref format,
                _callback,
                GCHandle.ToIntPtr(_selfHandle),
                CallbackFunction);

            if (result != MmsyserrNoError || _handle == IntPtr.Zero)
            {
                _handle = IntPtr.Zero;
                _selfHandle.Free();
                throw new InvalidOperationException($"Unable to open the audio input device. winmm error: {result}.");
            }

            try
            {
                PrepareBuffers();
                result = waveInStart(_handle);
                if (result != MmsyserrNoError)
                {
                    throw new InvalidOperationException($"Unable to start audio capture. winmm error: {result}.");
                }
            }
            catch
            {
                CloseUnsafe();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            _stopping = true;
            waveInStop(_handle);
            waveInReset(_handle);
            CloseUnsafe();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void PrepareBuffers()
    {
        var bytesPerBuffer = Math.Max(2, _sampleRate * 2 * _bufferMilliseconds / 1000);
        var headerSize = Marshal.SizeOf<WaveHdr>();

        for (var index = 0; index < _bufferCount; index++)
        {
            var headerPointer = Marshal.AllocHGlobal(headerSize);
            var dataPointer = Marshal.AllocHGlobal(bytesPerBuffer);

            var header = new WaveHdr
            {
                Data = dataPointer,
                BufferLength = (uint)bytesPerBuffer
            };

            Marshal.StructureToPtr(header, headerPointer, false);

            var result = waveInPrepareHeader(_handle, headerPointer, (uint)headerSize);
            if (result != MmsyserrNoError)
            {
                Marshal.FreeHGlobal(dataPointer);
                Marshal.FreeHGlobal(headerPointer);
                throw new InvalidOperationException($"Unable to prepare an audio buffer. winmm error: {result}.");
            }

            _slots[index] = new BufferSlot(headerPointer, dataPointer, bytesPerBuffer);

            result = waveInAddBuffer(_handle, headerPointer, (uint)headerSize);
            if (result != MmsyserrNoError)
            {
                throw new InvalidOperationException($"Unable to queue an audio buffer. winmm error: {result}.");
            }
        }
    }

    private void OnWaveInCallback(IntPtr handle, int message, IntPtr instance, IntPtr parameter1, IntPtr parameter2)
    {
        if (message != MmWimData || instance == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (_stopping)
            {
                return;
            }

            var slotIndex = FindSlot(parameter1);
            if (slotIndex < 0)
            {
                return;
            }

            var header = Marshal.PtrToStructure<WaveHdr>(parameter1);
            var bytesRecorded = (int)Math.Min(header.BytesRecorded, header.BufferLength);
            var sampleCount = bytesRecorded / sizeof(short);

            if (sampleCount > 0)
            {
                var samples = new short[sampleCount];
                Marshal.Copy(header.Data, samples, 0, sampleCount);

                var floatSamples = new float[sampleCount];
                const float scale = 1f / 32768f;
                for (var index = 0; index < sampleCount; index++)
                {
                    floatSamples[index] = samples[index] * scale;
                }

                DataAvailable?.Invoke(this, new AudioSamplesEventArgs(floatSamples));
            }

            var result = waveInAddBuffer(handle, parameter1, (uint)Marshal.SizeOf<WaveHdr>());
            if (result != MmsyserrNoError)
            {
                System.Diagnostics.Debug.WriteLine($"waveInAddBuffer failed: {result}");
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    private int FindSlot(IntPtr headerPointer)
    {
        for (var index = 0; index < _slots.Length; index++)
        {
            if (_slots[index].HeaderPointer == headerPointer)
            {
                return index;
            }
        }

        return -1;
    }

    private void CloseUnsafe()
    {
        var headerSize = Marshal.SizeOf<WaveHdr>();
        foreach (var slot in _slots)
        {
            if (slot.HeaderPointer == IntPtr.Zero)
            {
                continue;
            }

            waveInUnprepareHeader(_handle, slot.HeaderPointer, (uint)headerSize);
            Marshal.FreeHGlobal(slot.DataPointer);
            Marshal.FreeHGlobal(slot.HeaderPointer);
            slot.HeaderPointer = IntPtr.Zero;
            slot.DataPointer = IntPtr.Zero;
        }

        if (_handle != IntPtr.Zero)
        {
            waveInClose(_handle);
            _handle = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private sealed class BufferSlot(IntPtr headerPointer, IntPtr dataPointer, int bufferLength)
    {
        public IntPtr HeaderPointer { get; set; } = headerPointer;
        public IntPtr DataPointer { get; set; } = dataPointer;
        public int BufferLength { get; } = bufferLength;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WaveInProc(IntPtr handle, int message, IntPtr instance, IntPtr parameter1, IntPtr parameter2);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveInCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;

        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
    }

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private static extern int waveInGetDevCaps(IntPtr deviceId, out WaveInCaps caps, int capsSize);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInOpen(
        out IntPtr handle,
        IntPtr deviceId,
        ref WaveFormatEx format,
        WaveInProc callback,
        IntPtr instance,
        int flags);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, uint headerSize);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInStart(IntPtr handle);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInStop(IntPtr handle);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInReset(IntPtr handle);

    [DllImport("winmm.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int waveInClose(IntPtr handle);
}
