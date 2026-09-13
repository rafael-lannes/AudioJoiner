using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using AudioJoiner.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioJoiner.Services;

public class AudioEngineService : IAudioEngineService
{
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly DeviceNotificationClient _notificationClient;
    private readonly SettingsService _settingsService;
    private readonly Dispatcher _dispatcher;

    private WasapiLoopbackCapture? _mainCapture;
    private readonly Dictionary<string, WasapiLoopbackCapture> _groupCaptures = new();
    private readonly Dictionary<string, AudioOutputWorker> _outputWorkers = new();
    private readonly object _workersLock = new();
    private readonly System.Timers.Timer _meterTimer;

    private float _masterVolume = 1.0f;
    private int _latencyMs = 25;
    private bool _isRunning;
    private float _masterPeak;
    private bool _isDisposed;

    public VirtualDeviceService VirtualService { get; } = new();

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = new();
    public ObservableCollection<AudioGroupInfo> Groups { get; } = new();
    public AudioDeviceInfo? SelectedSourceDevice { get; set; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                StateChanged?.Invoke();
            }
        }
    }

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1.5f);
            UpdateAllWorkersVolume();
            StateChanged?.Invoke();
        }
    }

    public int LatencyMs
    {
        get => _latencyMs;
        set
        {
            _latencyMs = Math.Clamp(value, 10, 100);
            if (IsRunning)
            {
                RestartMirroring();
            }
        }
    }

    public float MasterPeakLevel => _masterPeak;

    public event Action? StateChanged;
    public event Action<string>? DeviceStatusChanged;
    public event Action<string>? ErrorOccurred;

    public AudioEngineService(SettingsService settingsService, Dispatcher dispatcher)
    {
        _settingsService = settingsService;
        _dispatcher = dispatcher;
        _masterVolume = _settingsService.CurrentSettings.MasterVolume;
        _latencyMs = _settingsService.CurrentSettings.LatencyMs;

        _deviceEnumerator = new MMDeviceEnumerator();
        _notificationClient = new DeviceNotificationClient();
        _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);

        _notificationClient.DevicesChanged += OnHardwareDevicesChanged;
        _notificationClient.DefaultRenderDeviceChanged += OnDefaultRenderDeviceChanged;

        _meterTimer = new System.Timers.Timer(33);
        _meterTimer.Elapsed += (s, e) => UpdateMeters();
        _meterTimer.AutoReset = true;
        _meterTimer.Start();

        RefreshDevices();
        LoadGroupsFromSettings();
    }

    public void RefreshDevices()
    {
        _dispatcher.Invoke(() =>
        {
            try
            {
                var currentDefault = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var activeEndpoints = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

                var existingMap = Devices.ToDictionary(d => d.Id);
                var newDeviceIds = new HashSet<string>();

                foreach (var endpoint in activeEndpoints)
                {
                    string id = endpoint.ID;
                    newDeviceIds.Add(id);

                    bool isDefault = currentDefault != null && id == currentDefault.ID;

                    if (existingMap.TryGetValue(id, out var existingDevice))
                    {
                        existingDevice.IsDefault = isDefault;
                        existingDevice.IsAvailable = true;
                        existingDevice.Name = endpoint.FriendlyName;
                        try
                        {
                            var mix = endpoint.AudioClient.MixFormat;
                            existingDevice.SampleRate = mix.SampleRate;
                            existingDevice.Channels = mix.Channels;
                            existingDevice.BitsPerSample = mix.BitsPerSample;
                        }
                        catch { }
                    }
                    else
                    {
                        var info = new AudioDeviceInfo
                        {
                            Id = id,
                            Name = endpoint.FriendlyName,
                            AdapterName = endpoint.DeviceTopology?.ToString() ?? "Dispositivo de Áudio",
                            IsDefault = isDefault,
                            IconType = ClassifyDeviceIcon(endpoint.FriendlyName),
                            IsAvailable = true,
                            Volume = 1.0f,
                            StatusText = "Pronto"
                        };

                        try
                        {
                            var mix = endpoint.AudioClient.MixFormat;
                            info.SampleRate = mix.SampleRate;
                            info.Channels = mix.Channels;
                            info.BitsPerSample = mix.BitsPerSample;
                        }
                        catch { }

                        var saved = _settingsService.CurrentSettings.MirrorDevices.FirstOrDefault(s => s.DeviceId == id);
                        if (saved != null)
                        {
                            info.IsMirrorEnabled = saved.IsEnabled;
                            info.Volume = saved.Volume;
                        }

                        Devices.Add(info);
                    }
                }

                foreach (var dev in Devices)
                {
                    if (!newDeviceIds.Contains(dev.Id))
                    {
                        dev.IsAvailable = false;
                        dev.StatusText = "Desconectado";
                    }
                }

                // Verificar se dispositivo virtual do grupo é o padrão do Windows
                if (currentDefault != null)
                {
                    foreach (var grp in Groups)
                    {
                        grp.IsWindowsDefault = grp.VirtualDeviceId == currentDefault.ID;
                    }
                }

                string savedSourceId = _settingsService.CurrentSettings.SelectedSourceDeviceId;
                if (SelectedSourceDevice == null || !SelectedSourceDevice.IsAvailable)
                {
                    if (savedSourceId == "DEFAULT" || string.IsNullOrEmpty(savedSourceId))
                    {
                        SelectedSourceDevice = Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
                    }
                    else
                    {
                        SelectedSourceDevice = Devices.FirstOrDefault(d => d.Id == savedSourceId) ?? Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
                    }
                }

                UpdateSourceFlag();
                SyncGroupMembers();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing devices: {ex.Message}");
            }
        });
    }

    private void LoadGroupsFromSettings()
    {
        Groups.Clear();
        foreach (var groupSetting in _settingsService.CurrentSettings.Groups)
        {
            var group = new AudioGroupInfo
            {
                Id = groupSetting.Id,
                Name = groupSetting.Name,
                IsEnabled = groupSetting.IsEnabled,
                Volume = groupSetting.Volume,
                VirtualDeviceId = groupSetting.VirtualDeviceId,
                VirtualDeviceName = groupSetting.VirtualDeviceName,
                MemberVolumes = groupSetting.MemberVolumes ?? new()
            };

            foreach (var devId in groupSetting.DeviceIds)
            {
                var dev = Devices.FirstOrDefault(d => d.Id == devId);
                if (dev != null)
                {
                    group.Members.Add(dev);
                }
            }
            group.NotifyMembersChanged();
            Groups.Add(group);
        }
    }

    private void SyncGroupMembers()
    {
        foreach (var group in Groups)
        {
            var groupSetting = _settingsService.CurrentSettings.Groups.FirstOrDefault(g => g.Id == group.Id);
            if (groupSetting != null)
            {
                group.Members.Clear();
                foreach (var devId in groupSetting.DeviceIds)
                {
                    var dev = Devices.FirstOrDefault(d => d.Id == devId);
                    if (dev != null)
                    {
                        group.Members.Add(dev);
                    }
                }
                group.NotifyMembersChanged();
            }
        }
    }

    private void UpdateSourceFlag()
    {
        foreach (var dev in Devices)
        {
            dev.IsSource = (SelectedSourceDevice != null && dev.Id == SelectedSourceDevice.Id);
            if (dev.IsSource)
            {
                dev.StatusText = IsRunning ? "Capturando Som..." : "Dispositivo de Origem";
            }
            else if (dev.IsMirrorEnabled || IsDeviceInActiveGroup(dev.Id))
            {
                dev.StatusText = IsRunning ? "Espelhando Ativo" : "Pronto para Espelhar";
            }
            else
            {
                dev.StatusText = "Inativo";
            }
        }
    }

    private bool IsDeviceInActiveGroup(string deviceId)
    {
        return Groups.Any(g => g.IsEnabled && g.Members.Any(m => m.Id == deviceId));
    }

    private float GetDeviceEffectiveVolume(string deviceId)
    {
        var activeGroup = Groups.FirstOrDefault(g => g.IsEnabled && g.Members.Any(m => m.Id == deviceId));
        if (activeGroup != null)
        {
            float memberVol = activeGroup.MemberVolumes.TryGetValue(deviceId, out float mv) ? mv : 1.0f;
            return activeGroup.Volume * memberVol;
        }

        var dev = Devices.FirstOrDefault(d => d.Id == deviceId);
        return dev?.Volume ?? 1.0f;
    }

    private DeviceIconType ClassifyDeviceIcon(string friendlyName)
    {
        string lower = friendlyName.ToLowerInvariant();
        if (lower.Contains("fone") || lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("earphone") || lower.Contains("airpod") || lower.Contains("buds"))
            return DeviceIconType.Headphones;
        if (lower.Contains("hdmi") || lower.Contains("display") || lower.Contains("monitor") || lower.Contains("tv") || lower.Contains("nvidia") || lower.Contains("amd"))
            return DeviceIconType.Monitor;
        if (lower.Contains("bluetooth") || lower.Contains("bt "))
            return DeviceIconType.Bluetooth;
        if (lower.Contains("usb") || lower.Contains("dac"))
            return DeviceIconType.Usb;

        return DeviceIconType.Speaker;
    }

    public void StartMirroring()
    {
        if (IsRunning) return;

        try
        {
            if (SelectedSourceDevice == null)
            {
                RefreshDevices();
                if (SelectedSourceDevice == null)
                {
                    throw new InvalidOperationException("Nenhum dispositivo de áudio de origem disponível.");
                }
            }

            // 1. Iniciar Captura Master Loopback
            MMDevice sourceMMDevice = _deviceEnumerator.GetDevice(SelectedSourceDevice.Id);
            _mainCapture = new WasapiLoopbackCapture(sourceMMDevice);
            WaveFormat mainCaptureFormat = _mainCapture.WaveFormat;

            lock (_workersLock)
            {
                _outputWorkers.Clear();

                // Identificar saídas necessárias
                var targetDeviceIds = new HashSet<string>();

                // Saídas individuais
                foreach (var device in Devices.Where(d => d.IsMirrorEnabled && !d.IsSource && d.IsAvailable))
                {
                    targetDeviceIds.Add(device.Id);
                }

                // Saídas de grupos
                foreach (var group in Groups.Where(g => g.IsEnabled))
                {
                    foreach (var member in group.Members.Where(m => !m.IsSource && m.IsAvailable))
                    {
                        targetDeviceIds.Add(member.Id);
                    }
                }

                foreach (var deviceId in targetDeviceIds)
                {
                    var device = Devices.FirstOrDefault(d => d.Id == deviceId);
                    if (device == null) continue;

                    try
                    {
                        MMDevice targetMMDevice = _deviceEnumerator.GetDevice(deviceId);
                        float effectiveVol = GetDeviceEffectiveVolume(deviceId);

                        var worker = new AudioOutputWorker(targetMMDevice, mainCaptureFormat, _latencyMs, effectiveVol, _masterVolume);
                        worker.PlaybackError += OnWorkerPlaybackError;
                        worker.Start();

                        _outputWorkers[deviceId] = worker;
                        device.StatusText = "Espelhando Ativo";
                    }
                    catch (Exception ex)
                    {
                        device.StatusText = "Falha ao Iniciar";
                        System.Diagnostics.Debug.WriteLine($"Failed to start worker for {device.Name}: {ex.Message}");
                    }
                }
            }

            _mainCapture.DataAvailable += OnMainCaptureDataAvailable;
            _mainCapture.RecordingStopped += OnCaptureRecordingStopped;
            _mainCapture.StartRecording();

            // 2. Iniciar Capturas Dedicadas para Grupos com Dispositivo Virtual do Windows
            foreach (var group in Groups.Where(g => g.IsEnabled && g.IsWindowsDeviceLinked))
            {
                StartGroupVirtualCapture(group, mainCaptureFormat);
            }

            IsRunning = true;
            UpdateSourceFlag();
        }
        catch (Exception ex)
        {
            StopMirroring();
            ErrorOccurred?.Invoke($"Erro ao iniciar espelhamento de áudio: {ex.Message}");
        }
    }

    private void StartGroupVirtualCapture(AudioGroupInfo group, WaveFormat defaultFormat)
    {
        if (string.IsNullOrEmpty(group.VirtualDeviceId)) return;

        try
        {
            MMDevice virtualDevice = _deviceEnumerator.GetDevice(group.VirtualDeviceId);
            var capture = new WasapiLoopbackCapture(virtualDevice);

            string groupId = group.Id;
            capture.DataAvailable += (s, e) =>
            {
                if (e.BytesRecorded <= 0) return;

                // Transmitir buffer apenas para os membros desse grupo
                lock (_workersLock)
                {
                    var targetGroup = Groups.FirstOrDefault(g => g.Id == groupId);
                    if (targetGroup != null && targetGroup.IsEnabled)
                    {
                        foreach (var member in targetGroup.Members)
                        {
                            if (_outputWorkers.TryGetValue(member.Id, out var worker))
                            {
                                worker.AddSamples(e.Buffer, 0, e.BytesRecorded);
                            }
                        }
                    }
                }
            };

            capture.StartRecording();
            _groupCaptures[group.Id] = capture;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erro ao iniciar captura do dispositivo virtual do grupo {group.Name}: {ex.Message}");
        }
    }

    public void StopMirroring()
    {
        try
        {
            if (_mainCapture != null)
            {
                _mainCapture.DataAvailable -= OnMainCaptureDataAvailable;
                _mainCapture.RecordingStopped -= OnCaptureRecordingStopped;
                try { _mainCapture.StopRecording(); } catch { }
                try { _mainCapture.Dispose(); } catch { }
                _mainCapture = null;
            }

            foreach (var kvp in _groupCaptures)
            {
                try { kvp.Value.StopRecording(); } catch { }
                try { kvp.Value.Dispose(); } catch { }
            }
            _groupCaptures.Clear();

            lock (_workersLock)
            {
                foreach (var worker in _outputWorkers.Values)
                {
                    worker.PlaybackError -= OnWorkerPlaybackError;
                    worker.Stop();
                    worker.Dispose();
                }
                _outputWorkers.Clear();
            }

            _masterPeak = 0f;
            foreach (var dev in Devices)
            {
                dev.PeakLevel = 0f;
            }
            foreach (var grp in Groups)
            {
                grp.PeakLevel = 0f;
            }

            IsRunning = false;
            UpdateSourceFlag();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping mirroring: {ex.Message}");
        }
    }

    private void RestartMirroring()
    {
        if (IsRunning)
        {
            StopMirroring();
            StartMirroring();
        }
    }

    public void SetDeviceMirrorState(string deviceId, bool isEnabled)
    {
        var device = Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null || device.IsSource) return;

        device.IsMirrorEnabled = isEnabled;
        SyncActiveWorkers();
        SaveDeviceSettings();
    }

    public void SetDeviceVolume(string deviceId, float volume)
    {
        var device = Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device != null)
        {
            device.Volume = volume;
            UpdateSingleWorkerVolume(deviceId);
            SaveDeviceSettings();
        }
    }

    public void CreateOrUpdateGroup(string? id, string name, List<string> deviceIds, float volume = 1.0f, string? virtualDeviceId = null, string? virtualDeviceName = null)
    {
        var existing = !string.IsNullOrEmpty(id) ? Groups.FirstOrDefault(g => g.Id == id) : null;
        if (existing != null)
        {
            existing.Name = name;
            existing.Volume = volume;
            existing.VirtualDeviceId = virtualDeviceId;
            existing.VirtualDeviceName = virtualDeviceName;
            existing.Members.Clear();
            foreach (var devId in deviceIds)
            {
                var dev = Devices.FirstOrDefault(d => d.Id == devId);
                if (dev != null) existing.Members.Add(dev);
            }
            existing.NotifyMembersChanged();
        }
        else
        {
            var newGroup = new AudioGroupInfo
            {
                Id = id ?? Guid.NewGuid().ToString(),
                Name = name,
                IsEnabled = true,
                Volume = volume,
                VirtualDeviceId = virtualDeviceId,
                VirtualDeviceName = virtualDeviceName
            };
            foreach (var devId in deviceIds)
            {
                var dev = Devices.FirstOrDefault(d => d.Id == devId);
                if (dev != null) newGroup.Members.Add(dev);
            }
            newGroup.NotifyMembersChanged();
            Groups.Add(newGroup);
        }

        SyncActiveWorkers();
        SaveDeviceSettings();
    }

    public void DeleteGroup(string groupId)
    {
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            if (_groupCaptures.TryGetValue(groupId, out var capture))
            {
                try { capture.StopRecording(); capture.Dispose(); } catch { }
                _groupCaptures.Remove(groupId);
            }

            Groups.Remove(group);
            SyncActiveWorkers();
            SaveDeviceSettings();
        }
    }

    public void SetGroupState(string groupId, bool isEnabled)
    {
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            group.IsEnabled = isEnabled;
            SyncActiveWorkers();
            SaveDeviceSettings();
        }
    }

    public void SetGroupVolume(string groupId, float volume)
    {
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            group.Volume = volume;
            foreach (var member in group.Members)
            {
                UpdateSingleWorkerVolume(member.Id);
            }
            SaveDeviceSettings();
        }
    }

    public void SetGroupMemberVolume(string groupId, string deviceId, float volume)
    {
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            group.MemberVolumes[deviceId] = volume;
            UpdateSingleWorkerVolume(deviceId);
            SaveDeviceSettings();
        }
    }

    public bool SetGroupAsDefaultWindowsDevice(string groupId)
    {
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null && !string.IsNullOrEmpty(group.VirtualDeviceId))
        {
            bool ok = VirtualService.SetAsDefaultWindowsPlaybackDevice(group.VirtualDeviceId);
            if (ok)
            {
                RefreshDevices();
                DeviceStatusChanged?.Invoke($"Dispositivo '{group.VirtualDeviceName ?? group.Name}' definido como saída padrão do Windows.");
            }
            return ok;
        }
        return false;
    }

    private void SyncActiveWorkers()
    {
        if (!IsRunning || _mainCapture == null)
        {
            UpdateSourceFlag();
            return;
        }

        var neededDeviceIds = new HashSet<string>();

        foreach (var dev in Devices.Where(d => d.IsMirrorEnabled && !d.IsSource && d.IsAvailable))
        {
            neededDeviceIds.Add(dev.Id);
        }

        foreach (var grp in Groups.Where(g => g.IsEnabled))
        {
            foreach (var member in grp.Members.Where(m => !m.IsSource && m.IsAvailable))
            {
                neededDeviceIds.Add(member.Id);
            }
        }

        lock (_workersLock)
        {
            var toRemove = _outputWorkers.Keys.Where(id => !neededDeviceIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                if (_outputWorkers.TryGetValue(id, out var worker))
                {
                    worker.PlaybackError -= OnWorkerPlaybackError;
                    worker.Stop();
                    worker.Dispose();
                    _outputWorkers.Remove(id);

                    var dev = Devices.FirstOrDefault(d => d.Id == id);
                    if (dev != null)
                    {
                        dev.StatusText = "Inativo";
                        dev.PeakLevel = 0f;
                    }
                }
            }

            foreach (var id in neededDeviceIds)
            {
                if (!_outputWorkers.ContainsKey(id))
                {
                    var dev = Devices.FirstOrDefault(d => d.Id == id);
                    if (dev != null && dev.IsAvailable)
                    {
                        try
                        {
                            MMDevice targetMMDevice = _deviceEnumerator.GetDevice(id);
                            float effectiveVol = GetDeviceEffectiveVolume(id);

                            var worker = new AudioOutputWorker(targetMMDevice, _mainCapture.WaveFormat, _latencyMs, effectiveVol, _masterVolume);
                            worker.PlaybackError += OnWorkerPlaybackError;
                            worker.Start();
                            _outputWorkers[id] = worker;
                            dev.StatusText = "Espelhando Ativo";
                        }
                        catch (Exception ex)
                        {
                            dev.StatusText = "Falha ao Iniciar";
                            ErrorOccurred?.Invoke($"Erro ao conectar ao dispositivo '{dev.Name}': {ex.Message}");
                        }
                    }
                }
                else
                {
                    UpdateSingleWorkerVolume(id);
                }
            }
        }

        UpdateSourceFlag();
    }

    private void UpdateSingleWorkerVolume(string deviceId)
    {
        lock (_workersLock)
        {
            if (_outputWorkers.TryGetValue(deviceId, out var worker))
            {
                float effectiveVol = GetDeviceEffectiveVolume(deviceId);
                worker.SetVolume(effectiveVol, _masterVolume);
            }
        }
    }

    private void UpdateAllWorkersVolume()
    {
        lock (_workersLock)
        {
            foreach (var kvp in _outputWorkers)
            {
                float effectiveVol = GetDeviceEffectiveVolume(kvp.Key);
                kvp.Value.SetVolume(effectiveVol, _masterVolume);
            }
        }
    }

    private void OnMainCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        float peak = 0f;
        int floatCount = e.BytesRecorded / 4;
        for (int i = 0; i < floatCount; i += 8)
        {
            float val = Math.Abs(BitConverter.ToSingle(e.Buffer, i * 4));
            if (val > peak) peak = val;
        }

        _masterPeak = Math.Max(_masterPeak * 0.7f, peak);

        // Distribuir para saídas individuais e grupos não-virtuais
        lock (_workersLock)
        {
            foreach (var worker in _outputWorkers.Values)
            {
                worker.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }

    private void UpdateMeters()
    {
        if (!IsRunning) return;

        _dispatcher.BeginInvoke(() =>
        {
            _masterPeak *= 0.85f;
            if (_masterPeak < 0.005f) _masterPeak = 0f;

            if (SelectedSourceDevice != null)
            {
                SelectedSourceDevice.PeakLevel = _masterPeak;
            }

            lock (_workersLock)
            {
                foreach (var kvp in _outputWorkers)
                {
                    var dev = Devices.FirstOrDefault(d => d.Id == kvp.Key);
                    if (dev != null)
                    {
                        dev.PeakLevel = kvp.Value.PeakLevel;
                    }
                }
            }

            foreach (var group in Groups)
            {
                if (group.Members.Count > 0)
                {
                    group.PeakLevel = group.Members.Max(m => m.PeakLevel);
                }
                else
                {
                    group.PeakLevel = 0f;
                }
            }
        });
    }

    private void OnWorkerPlaybackError(AudioOutputWorker worker, Exception ex)
    {
        _dispatcher.Invoke(() =>
        {
            var device = Devices.FirstOrDefault(d => d.Id == worker.DeviceId);
            if (device != null)
            {
                device.StatusText = "Erro / Desconectado";
                device.PeakLevel = 0f;
            }

            lock (_workersLock)
            {
                _outputWorkers.Remove(worker.DeviceId);
                worker.Dispose();
            }

            DeviceStatusChanged?.Invoke($"Dispositivo '{worker.DeviceFriendlyName}' desconectou ou apresentou erro.");
        });
    }

    private void OnCaptureRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _dispatcher.Invoke(() =>
            {
                ErrorOccurred?.Invoke($"Captura de áudio interrompida: {e.Exception.Message}");
                StopMirroring();
            });
        }
    }

    private void OnHardwareDevicesChanged()
    {
        RefreshDevices();
        SyncActiveWorkers();
    }

    private void OnDefaultRenderDeviceChanged(string defaultDeviceId)
    {
        if (_settingsService.CurrentSettings.SelectedSourceDeviceId == "DEFAULT" || string.IsNullOrEmpty(_settingsService.CurrentSettings.SelectedSourceDeviceId))
        {
            RefreshDevices();
            if (IsRunning)
            {
                RestartMirroring();
            }
        }
    }

    private void SaveDeviceSettings()
    {
        _settingsService.CurrentSettings.MasterVolume = _masterVolume;
        _settingsService.CurrentSettings.LatencyMs = _latencyMs;
        _settingsService.CurrentSettings.SelectedSourceDeviceId = SelectedSourceDevice?.Id ?? "DEFAULT";
        _settingsService.CurrentSettings.MirrorDevices = Devices
            .Select(d => new DeviceSetting
            {
                DeviceId = d.Id,
                IsEnabled = d.IsMirrorEnabled,
                Volume = d.Volume
            })
            .ToList();

        _settingsService.CurrentSettings.Groups = Groups
            .Select(g => new DeviceGroupSetting
            {
                Id = g.Id,
                Name = g.Name,
                IsEnabled = g.IsEnabled,
                Volume = g.Volume,
                VirtualDeviceId = g.VirtualDeviceId,
                VirtualDeviceName = g.VirtualDeviceName,
                DeviceIds = g.Members.Select(m => m.Id).ToList(),
                MemberVolumes = g.MemberVolumes
            })
            .ToList();

        _settingsService.SaveSettings();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _meterTimer.Stop();
        _meterTimer.Dispose();

        StopMirroring();

        try
        {
            _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            _deviceEnumerator.Dispose();
        }
        catch { }
    }
}
