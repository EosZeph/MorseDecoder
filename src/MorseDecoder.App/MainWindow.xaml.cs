using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MorseDecoder.App.Rendering;
using MorseDecoder.App.ViewModels;
using MorseDecoder.Core.Audio;
using MorseDecoder.Core.Dsp;
using MorseDecoder.Core.Synthetic;

namespace MorseDecoder.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int SampleRate = 48_000;
    private const int FftSize = 1024;
    private const int HopSize = 256;

    private readonly DispatcherTimer _uiTimer;
    private readonly WaterfallBitmap _waterfall;
    private MorsePipeline? _pipeline;
    private WaveInRecorder? _recorder;
    private SyntheticCwSource? _syntheticSource;
    private TrackRow? _selectedTrack;
    private long _waterfallFrameCount;
    private long _lastFrameCount;
    private DateTime _lastFrameTime = DateTime.UtcNow;
    private int _statusTick;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _waterfall = new WaterfallBitmap(width: 520, height: 240, minimumBin: 2, maximumBin: 83);
        WaterfallImage.Source = _waterfall.Bitmap;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(35)
        };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<TrackRow> Tracks { get; } = [];

    public TrackRow? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (ReferenceEquals(_selectedTrack, value))
            {
                return;
            }

            _selectedTrack = value;
            OnPropertyChanged();
            UpdateSelectedTranscript();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAudioDevices();
    }

    private void RefreshAudioDevices()
    {
        try
        {
            var devices = WaveInRecorder.GetDevices();
            DeviceComboBox.ItemsSource = devices;
            DeviceComboBox.SelectedIndex = devices.Count > 0 ? 0 : -1;
            StartButton.IsEnabled = devices.Count > 0;

            if (devices.Count == 0)
            {
                StatusText.Text = "未找到音频输入设备";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"无法枚举音频设备: {exception.Message}";
            StartButton.IsEnabled = false;
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceComboBox.SelectedItem is not AudioInputDevice device)
        {
            MessageBox.Show(this, "请选择一个音频输入设备。", "Morse CW Decoder");
            return;
        }

        await StopCaptureAsync();
        StartPipeline();

        try
        {
            _recorder = new WaveInRecorder(SampleRate, bufferMilliseconds: 20, bufferCount: 5);
            _recorder.DataAvailable += Recorder_DataAvailable;
            _recorder.Start(device.DeviceId);

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            TestButton.IsEnabled = false;
            StatusText.Text = $"正在采集: {device.Name}";
        }
        catch (Exception exception)
        {
            await StopCaptureAsync();
            MessageBox.Show(this, exception.Message, "音频输入错误");
            StatusText.Text = "音频输入启动失败";
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopCaptureAsync();
        StatusText.Text = "已停止";
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pipeline is not null)
        {
            ClearWorkspace();
        }
        else
        {
            StartPipeline();
        }

        _syntheticSource = new SyntheticCwSource(
            block => _pipeline?.Enqueue(block),
            SampleRate,
            blockMilliseconds: 20);
        _syntheticSource.Start();

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        TestButton.IsEnabled = false;
        StatusText.Text = "测试信号: 700 Hz / 20 WPM";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearWorkspace();
    }

    private void ClearWorkspace()
    {
        _pipeline?.Reset();
        _waterfall.Clear();
        Tracks.Clear();
        SelectedTrack = null;
        SelectedTrackHeader.Text = "请选择一条信号轨迹";
        TranscriptTextBox.Clear();
        _waterfallFrameCount = 0;
        _lastFrameCount = 0;
    }

    private void StartPipeline()
    {
        _pipeline = new MorsePipeline(SampleRate, FftSize, HopSize);
        _pipeline.Start();
    }

    private async Task StopCaptureAsync()
    {
        if (_recorder is not null)
        {
            _recorder.DataAvailable -= Recorder_DataAvailable;
            _recorder.Dispose();
            _recorder = null;
        }

        _syntheticSource?.Dispose();
        _syntheticSource = null;

        if (_pipeline is not null)
        {
            await _pipeline.DisposeAsync();
            _pipeline = null;
        }

        StartButton.IsEnabled = DeviceComboBox.Items.Count > 0;
        StopButton.IsEnabled = false;
        TestButton.IsEnabled = true;
    }

    private void Recorder_DataAvailable(object? sender, AudioSamplesEventArgs e)
    {
        _pipeline?.Enqueue(e.Samples);
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (_pipeline is not null)
        {
            while (_pipeline.TryDequeueWaterfallFrame(out var frame) && frame is not null)
            {
                _waterfall.Append(frame);
                _waterfallFrameCount++;
            }

            _waterfall.Render();

            _statusTick++;
            if (_statusTick >= 6)
            {
                _statusTick = 0;
                SynchronizeTracks();
                UpdateStatus();
            }
        }

        UpdateSelectedTranscript();
    }

    private void SynchronizeTracks()
    {
        if (_pipeline is null)
        {
            return;
        }

        var snapshots = _pipeline.GetTrackSnapshots();
        var snapshotIds = snapshots.Select(snapshot => snapshot.Id).ToHashSet();

        for (var index = Tracks.Count - 1; index >= 0; index--)
        {
            if (!snapshotIds.Contains(Tracks[index].Id))
            {
                if (ReferenceEquals(Tracks[index], SelectedTrack))
                {
                    SelectedTrack = null;
                }

                Tracks.RemoveAt(index);
            }
        }

        foreach (var snapshot in snapshots)
        {
            var row = Tracks.FirstOrDefault(item => item.Id == snapshot.Id);
            if (row is null)
            {
                row = new TrackRow(snapshot.Id);
                Tracks.Add(row);
            }

            row.Update(snapshot);
        }

        if (SelectedTrack is null && Tracks.Count > 0)
        {
            SelectedTrack = Tracks
                .OrderByDescending(row => row.Transcript.Length)
                .ThenByDescending(row => row.SignalStrengthDb)
                .First();
            TracksGrid.SelectedItem = SelectedTrack;
        }
    }

    private void UpdateStatus()
    {
        var now = DateTime.UtcNow;
        var elapsed = Math.Max(0.001, (now - _lastFrameTime).TotalSeconds);
        var frameRate = (_waterfallFrameCount - _lastFrameCount) / elapsed;
        _lastFrameCount = _waterfallFrameCount;
        _lastFrameTime = now;

        var dropped = _pipeline?.DroppedBlocks ?? 0;
        StatusText.Text =
            $"{frameRate:F1} 帧/秒 | 轨迹 {Tracks.Count} | 音频块丢弃 {dropped} | " +
            $"{SampleRate / 1000} kHz";
    }

    private void UpdateSelectedTranscript()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        SelectedTrackHeader.Text =
            $"轨迹 #{SelectedTrack.Id}  {SelectedTrack.FrequencyHz:F1} Hz  " +
            $"{SelectedTrack.Wpm:F1} WPM  {SelectedTrack.SignalStrengthDb:F1} dB";

        if (!string.Equals(TranscriptTextBox.Text, SelectedTrack.Transcript, StringComparison.Ordinal))
        {
            TranscriptTextBox.Text = SelectedTrack.Transcript;
            TranscriptTextBox.CaretIndex = TranscriptTextBox.Text.Length;
            TranscriptTextBox.ScrollToEnd();
        }
    }

    private void TracksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedTrack = TracksGrid.SelectedItem as TrackRow;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        e.Cancel = true;
        _uiTimer.Stop();
        await StopCaptureAsync();
        Application.Current.Shutdown();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
