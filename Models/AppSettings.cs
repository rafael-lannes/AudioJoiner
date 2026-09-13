namespace AudioJoiner.Models;

public class DeviceSetting
{
    public string DeviceId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;
    public float Volume { get; set; } = 1.0f;
}

public class DeviceGroupSetting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<string> DeviceIds { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public float Volume { get; set; } = 1.0f;
    public string? VirtualDeviceId { get; set; }
    public string? VirtualDeviceName { get; set; }
    public Dictionary<string, float> MemberVolumes { get; set; } = new();
}

public class AppSettings
{
    public string SelectedSourceDeviceId { get; set; } = "DEFAULT";
    public List<DeviceSetting> MirrorDevices { get; set; } = new();
    public List<DeviceGroupSetting> Groups { get; set; } = new();
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool AutoStartMirroring { get; set; } = true;
    public int LatencyMs { get; set; } = 25;
    public float MasterVolume { get; set; } = 1.0f;
}
