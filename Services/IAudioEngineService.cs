using System.Collections.ObjectModel;
using AudioJoiner.Models;

namespace AudioJoiner.Services;

public interface IAudioEngineService : IDisposable
{
    ObservableCollection<AudioDeviceInfo> Devices { get; }
    AudioDeviceInfo? SelectedSourceDevice { get; set; }
    bool IsRunning { get; }
    float MasterVolume { get; set; }
    int LatencyMs { get; set; }
    float MasterPeakLevel { get; }

    ObservableCollection<EqualizerBand> EqualizerBands { get; }
    List<EqualizerPreset> EqualizerPresets { get; }
    bool IsEqualizerEnabled { get; set; }
    bool IsEqualizerExpanded { get; set; }
    EqualizerPreset? SelectedEqualizerPreset { get; set; }

    event Action? StateChanged;
    event Action<string>? DeviceStatusChanged;
    event Action<string>? ErrorOccurred;

    void RefreshDevices();
    void StartMirroring();
    void StopMirroring();
    void SetDeviceMirrorState(string deviceId, bool isEnabled);
    void SetDeviceVolume(string deviceId, float volume);
    void EnableAllDevices();
    void DisableAllDevices();

    void SetEqualizerBandGain(int bandIndex, float gainDb);
    void ApplyEqualizerPreset(EqualizerPreset preset);
    void ResetEqualizer();
}
