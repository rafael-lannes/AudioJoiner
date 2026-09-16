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
    private readonly Dictionary<string, AudioOutputWorker> _outputWorkers = new();
    private readonly object _workersLock = new();
    private readonly System.Timers.Timer _meterTimer;

    private float _masterVolume = 1.0f;
    private int _latencyMs = 25;
    private bool _isRunning;
    private float _masterPeak;
    private bool _isDisposed;

    private bool _isEqualizerEnabled = true;
    private EqualizerPreset? _selectedEqualizerPreset;

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = new();
    public ObservableCollection<EqualizerBand> EqualizerBands { get; } = new();
    public List<EqualizerPreset> EqualizerPresets { get; } = EqualizerPreset.GetDefaultPresets();
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

    public bool IsEqualizerEnabled
    {
        get => _isEqualizerEnabled;
        set
        {
            if (_isEqualizerEnabled != value)
            {
                _isEqualizerEnabled = value;
                lock (_workersLock)
                {
                    foreach (var worker in _outputWorkers.Values)
                    {
                        worker.SetEqualizerEnabled(value);
                    }
                }
                SaveDeviceSettings();
                StateChanged?.Invoke();
            }
        }
    }

    public EqualizerPreset? SelectedEqualizerPreset
    {
        get => _selectedEqualizerPreset;
        set
        {
            if (_selectedEqualizerPreset != value)
            {
                _selectedEqualizerPreset = value;
                StateChanged?.Invoke();
            }
        }
    }

    public event Action? StateChanged;
    public event Action<string>? DeviceStatusChanged;
    public event Action<string>? ErrorOccurred;

    public AudioEngineService(SettingsService settingsService, Dispatcher dispatcher)
    {
        _settingsService = settingsService;
        _dispatcher = dispatcher;
        _masterVolume = _settingsService.CurrentSettings.MasterVolume;
        _latencyMs = _settingsService.CurrentSettings.LatencyMs;

        InitializeEqualizerBands();

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
    }

    private void InitializeEqualizerBands()
    {
        float[] defaultFrequencies = new float[] { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
        string[] defaultLabels = new string[] { "31Hz", "62Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz" };

        var eqSettings = _settingsService.CurrentSettings.Equalizer ?? new EqualizerSettings();
        _isEqualizerEnabled = eqSettings.IsEnabled;

        float[] savedGains = eqSettings.BandGains ?? new float[10];

        EqualizerBands.Clear();
        for (int i = 0; i < defaultFrequencies.Length; i++)
        {
            float gain = (i < savedGains.Length) ? savedGains[i] : 0f;
            EqualizerBands.Add(new EqualizerBand
            {
                Index = i,
                Label = defaultLabels[i],
                Frequency = defaultFrequencies[i],
                GainDb = gain,
                BandWidth = 1.0f
            });
        }

        string savedPresetName = eqSettings.SelectedPreset ?? "Flat (Padrão)";
        _selectedEqualizerPreset = EqualizerPresets.FirstOrDefault(p => p.Name == savedPresetName) ?? EqualizerPresets.First();
    }

    public void RefreshDevices()
    {
        void UpdateList()
        {
            try
            {
                MMDevice? currentDefault = null;
                try
                {
                    currentDefault = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                catch { }

                List<MMDevice> activeEndpoints = new();
                try
                {
                    activeEndpoints = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                }
                catch { }

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
                            AdapterName = "Dispositivo de Áudio",
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing devices: {ex.Message}");
            }
        }

        if (_dispatcher.CheckAccess())
        {
            UpdateList();
        }
        else
        {
            _dispatcher.Invoke(UpdateList);
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
            else if (dev.IsMirrorEnabled)
            {
                dev.StatusText = IsRunning ? "Espelhando Ativo" : "Pronto para Espelhar";
            }
            else
            {
                dev.StatusText = "Inativo";
            }
        }
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

            MMDevice sourceMMDevice = _deviceEnumerator.GetDevice(SelectedSourceDevice.Id);
            _mainCapture = new WasapiLoopbackCapture(sourceMMDevice);
            WaveFormat mainCaptureFormat = _mainCapture.WaveFormat;

            lock (_workersLock)
            {
                _outputWorkers.Clear();

                foreach (var device in Devices.Where(d => d.IsMirrorEnabled && !d.IsSource && d.IsAvailable))
                {
                    try
                    {
                        MMDevice targetMMDevice = _deviceEnumerator.GetDevice(device.Id);
                        var worker = new AudioOutputWorker(
                            targetMMDevice,
                            mainCaptureFormat,
                            _latencyMs,
                            device.Volume,
                            _masterVolume,
                            EqualizerBands,
                            _isEqualizerEnabled
                        );

                        worker.PlaybackError += OnWorkerPlaybackError;
                        worker.Start();

                        _outputWorkers[device.Id] = worker;
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

            IsRunning = true;
            UpdateSourceFlag();
        }
        catch (Exception ex)
        {
            StopMirroring();
            ErrorOccurred?.Invoke($"Erro ao iniciar espelhamento de áudio: {ex.Message}");
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
            lock (_workersLock)
            {
                if (_outputWorkers.TryGetValue(deviceId, out var worker))
                {
                    worker.SetVolume(volume, _masterVolume);
                }
            }
            SaveDeviceSettings();
        }
    }

    public void SetEqualizerBandGain(int bandIndex, float gainDb)
    {
        if (bandIndex >= 0 && bandIndex < EqualizerBands.Count)
        {
            EqualizerBands[bandIndex].GainDb = gainDb;
            lock (_workersLock)
            {
                foreach (var worker in _outputWorkers.Values)
                {
                    worker.SetEqualizerBand(bandIndex, gainDb);
                }
            }
            SaveDeviceSettings();
        }
    }

    public void ApplyEqualizerPreset(EqualizerPreset preset)
    {
        _selectedEqualizerPreset = preset;
        float[] gains = preset.Gains;

        for (int i = 0; i < Math.Min(gains.Length, EqualizerBands.Count); i++)
        {
            EqualizerBands[i].GainDb = gains[i];
        }

        lock (_workersLock)
        {
            foreach (var worker in _outputWorkers.Values)
            {
                worker.SetEqualizerAllBands(gains);
            }
        }

        SaveDeviceSettings();
        StateChanged?.Invoke();
    }

    public void ResetEqualizer()
    {
        var flatPreset = EqualizerPresets.FirstOrDefault(p => p.Name.Contains("Flat")) ?? new EqualizerPreset("Flat", new float[10]);
        ApplyEqualizerPreset(flatPreset);
    }

    private void SyncActiveWorkers()
    {
        if (!IsRunning || _mainCapture == null)
        {
            UpdateSourceFlag();
            return;
        }

        var neededDeviceIds = Devices
            .Where(d => d.IsMirrorEnabled && !d.IsSource && d.IsAvailable)
            .Select(d => d.Id)
            .ToHashSet();

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
                            var worker = new AudioOutputWorker(
                                targetMMDevice,
                                _mainCapture.WaveFormat,
                                _latencyMs,
                                dev.Volume,
                                _masterVolume,
                                EqualizerBands,
                                _isEqualizerEnabled
                            );

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
            }
        }

        UpdateSourceFlag();
    }

    private void UpdateAllWorkersVolume()
    {
        lock (_workersLock)
        {
            foreach (var kvp in _outputWorkers)
            {
                var dev = Devices.FirstOrDefault(d => d.Id == kvp.Key);
                float devVol = dev?.Volume ?? 1.0f;
                kvp.Value.SetVolume(devVol, _masterVolume);
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

        _settingsService.CurrentSettings.Equalizer = new EqualizerSettings
        {
            IsEnabled = _isEqualizerEnabled,
            SelectedPreset = _selectedEqualizerPreset?.Name ?? "Flat (Padrão)",
            BandGains = EqualizerBands.Select(b => b.GainDb).ToArray()
        };

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
