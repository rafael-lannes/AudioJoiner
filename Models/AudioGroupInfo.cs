using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioJoiner.Models;

public class AudioGroupInfo : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "Novo Grupo de Áudio";
    private bool _isEnabled = true;
    private float _volume = 1.0f;
    private float _peakLevel;
    private bool _isExpanded;
    private string? _virtualDeviceId;
    private string? _virtualDeviceName;
    private bool _isWindowsDefault;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
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

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public string? VirtualDeviceId
    {
        get => _virtualDeviceId;
        set
        {
            if (SetField(ref _virtualDeviceId, value))
            {
                OnPropertyChanged(nameof(IsWindowsDeviceLinked));
                OnPropertyChanged(nameof(VirtualDeviceStatus));
            }
        }
    }

    public string? VirtualDeviceName
    {
        get => _virtualDeviceName;
        set
        {
            if (SetField(ref _virtualDeviceName, value))
            {
                OnPropertyChanged(nameof(VirtualDeviceStatus));
            }
        }
    }

    public bool IsWindowsDefault
    {
        get => _isWindowsDefault;
        set => SetField(ref _isWindowsDefault, value);
    }

    public bool IsWindowsDeviceLinked => !string.IsNullOrEmpty(_virtualDeviceId);

    public string VirtualDeviceStatus => IsWindowsDeviceLinked
        ? $"Vinculado a: {_virtualDeviceName ?? "Dispositivo Virtual do Windows"}"
        : "Nenhum dispositivo virtual do Windows vinculado";

    public ObservableCollection<AudioDeviceInfo> Members { get; } = new();

    public Dictionary<string, float> MemberVolumes { get; set; } = new();

    public string SummaryText
    {
        get
        {
            int count = Members.Count;
            if (count == 0) return "Nenhum dispositivo associado";
            string names = string.Join(", ", Members.Select(m => m.Name));
            return $"{count} dispositivo{(count > 1 ? "s" : "")}: {names}";
        }
    }

    public void NotifyMembersChanged()
    {
        OnPropertyChanged(nameof(SummaryText));
    }

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
