using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioJoiner.Models;

public enum DeviceIconType
{
    Speaker,
    Headphones,
    Monitor,
    Bluetooth,
    Usb,
    General
}

public class AudioDeviceInfo : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _adapterName = string.Empty;
    private DeviceIconType _iconType = DeviceIconType.General;
    private bool _isDefault;
    private bool _isSource;
    private bool _isMirrorEnabled;
    private float _volume = 1.0f;
    private float _peakLevel;
    private string _statusText = "Pronto";
    private bool _isAvailable = true;
    private int _sampleRate = 48000;
    private int _channels = 2;
    private int _bitsPerSample = 32;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string AdapterName
    {
        get => _adapterName;
        set => SetField(ref _adapterName, value);
    }

    public DeviceIconType IconType
    {
        get => _iconType;
        set => SetField(ref _iconType, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set => SetField(ref _isDefault, value);
    }

    public bool IsSource
    {
        get => _isSource;
        set
        {
            if (SetField(ref _isSource, value))
            {
                OnPropertyChanged(nameof(CanBeMirrored));
            }
        }
    }

    public bool IsMirrorEnabled
    {
        get => _isMirrorEnabled;
        set => SetField(ref _isMirrorEnabled, value);
    }

    public float Volume
    {
        get => _volume;
        set
        {
            float clamped = Math.Clamp(value, 0f, 1.5f);
            if (SetField(ref _volume, clamped))
            {
                OnPropertyChanged(nameof(VolumePercentage));
            }
        }
    }

    public int VolumePercentage => (int)Math.Round(_volume * 100);

    public float PeakLevel
    {
        get => _peakLevel;
        set => SetField(ref _peakLevel, Math.Clamp(value, 0f, 1f));
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetField(ref _isAvailable, value);
    }

    public int SampleRate
    {
        get => _sampleRate;
        set => SetField(ref _sampleRate, value);
    }

    public int Channels
    {
        get => _channels;
        set => SetField(ref _channels, value);
    }

    public int BitsPerSample
    {
        get => _bitsPerSample;
        set => SetField(ref _bitsPerSample, value);
    }

    public string FormatDescription => $"{SampleRate / 1000.0:0.#} kHz • {(Channels == 2 ? "Estéreo" : Channels == 1 ? "Mono" : $"{Channels} canais")} • {BitsPerSample}-bit";

    public bool CanBeMirrored => !_isSource;

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
