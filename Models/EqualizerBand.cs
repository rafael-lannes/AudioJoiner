using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioJoiner.Models;

public class EqualizerBand : INotifyPropertyChanged
{
    private float _gainDb;

    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public float Frequency { get; set; }
    public float BandWidth { get; set; } = 0.8f;

    public float GainDb
    {
        get => _gainDb;
        set
        {
            float clamped = Math.Clamp(value, -12f, 12f);
            if (SetField(ref _gainDb, clamped))
            {
                OnPropertyChanged(nameof(GainDisplay));
            }
        }
    }

    public string GainDisplay => $"{GainDb:+0.0;-0.0;0.0} dB";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
