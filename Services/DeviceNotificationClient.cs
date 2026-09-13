using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioJoiner.Services;

public class DeviceNotificationClient : IMMNotificationClient
{
    public event Action? DevicesChanged;
    public event Action<string>? DefaultRenderDeviceChanged;
    public event Action<string, DeviceState>? DeviceStateChanged;

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        DevicesChanged?.Invoke();
    }

    public void OnDeviceRemoved(string deviceId)
    {
        DevicesChanged?.Invoke();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        DeviceStateChanged?.Invoke(deviceId, newState);
        DevicesChanged?.Invoke();
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            DefaultRenderDeviceChanged?.Invoke(defaultDeviceId);
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Alteração de propriedades (ex: renomear dispositivo ou alterar formato nas propriedades do Windows)
        DevicesChanged?.Invoke();
    }
}
