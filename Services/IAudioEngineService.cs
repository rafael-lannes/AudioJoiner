using System.Collections.ObjectModel;
using AudioJoiner.Models;

namespace AudioJoiner.Services;

public interface IAudioEngineService : IDisposable
{
    ObservableCollection<AudioDeviceInfo> Devices { get; }
    ObservableCollection<AudioGroupInfo> Groups { get; }
    AudioDeviceInfo? SelectedSourceDevice { get; set; }
    bool IsRunning { get; }
    float MasterVolume { get; set; }
    int LatencyMs { get; set; }
    float MasterPeakLevel { get; }

    VirtualDeviceService VirtualService { get; }

    event Action? StateChanged;
    event Action<string>? DeviceStatusChanged;
    event Action<string>? ErrorOccurred;

    void RefreshDevices();
    void StartMirroring();
    void StopMirroring();
    void SetDeviceMirrorState(string deviceId, bool isEnabled);
    void SetDeviceVolume(string deviceId, float volume);

    void CreateOrUpdateGroup(string? id, string name, List<string> deviceIds, float volume = 1.0f, string? virtualDeviceId = null, string? virtualDeviceName = null);
    void DeleteGroup(string groupId);
    void SetGroupState(string groupId, bool isEnabled);
    void SetGroupVolume(string groupId, float volume);
    void SetGroupMemberVolume(string groupId, string deviceId, float volume);
    bool SetGroupAsDefaultWindowsDevice(string groupId);
}
