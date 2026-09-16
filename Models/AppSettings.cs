namespace AudioJoiner.Models;

public class DeviceSetting
{
    public string DeviceId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;
    public float Volume { get; set; } = 1.0f;
}

public class EqualizerSettings
{
    public bool IsEnabled { get; set; } = true;
    public bool IsExpanded { get; set; } = false;
    public string SelectedPreset { get; set; } = "Flat (Padrão)";
    public float[] BandGains { get; set; } = new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
}

public class AppSettings
{
    public string SelectedSourceDeviceId { get; set; } = "DEFAULT";
    public List<DeviceSetting> MirrorDevices { get; set; } = new();
    public EqualizerSettings Equalizer { get; set; } = new();
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool AutoStartMirroring { get; set; } = true;
    public int LatencyMs { get; set; } = 25;
    public float MasterVolume { get; set; } = 1.0f;
}
