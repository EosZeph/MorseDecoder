using System.ComponentModel;
using System.Runtime.CompilerServices;
using MorseDecoder.Core.Dsp;

namespace MorseDecoder.App.ViewModels;

public sealed class TrackRow : INotifyPropertyChanged
{
    private double _frequencyHz;
    private double _signalStrengthDb;
    private double _wpm;
    private double _confidence;
    private bool _isActive;
    private string _transcript = string.Empty;

    public TrackRow(int id)
    {
        Id = id;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }

    public double FrequencyHz
    {
        get => _frequencyHz;
        private set => SetField(ref _frequencyHz, value);
    }

    public double SignalStrengthDb
    {
        get => _signalStrengthDb;
        private set => SetField(ref _signalStrengthDb, value);
    }

    public double Wpm
    {
        get => _wpm;
        private set => SetField(ref _wpm, value);
    }

    public double Confidence
    {
        get => _confidence;
        private set => SetField(ref _confidence, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    public string Transcript
    {
        get => _transcript;
        private set => SetField(ref _transcript, value);
    }

    public void Update(TrackSnapshot snapshot)
    {
        FrequencyHz = snapshot.FrequencyHz;
        SignalStrengthDb = snapshot.SignalStrengthDb;
        Wpm = snapshot.Wpm;
        Confidence = snapshot.Confidence;
        IsActive = snapshot.IsActive;
        Transcript = snapshot.Transcript;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

