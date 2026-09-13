using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace AudioJoiner.Services;

public class AudioOutputWorker : IDisposable
{
    private readonly MMDevice _device;
    private readonly WaveFormat _sourceWaveFormat;
    private readonly int _latencyMs;
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private MeteredVolumeSampleProvider? _meteredVolume;
    private float _deviceVolume = 1.0f;
    private float _masterVolume = 1.0f;
    private bool _isDisposed;
    private readonly object _lock = new();

    public string DeviceId => _device.ID;
    public string DeviceFriendlyName => _device.FriendlyName;
    public bool IsRunning { get; private set; }
    public float PeakLevel => _meteredVolume?.CurrentPeak ?? 0f;

    public event Action<AudioOutputWorker, Exception>? PlaybackError;

    public AudioOutputWorker(MMDevice device, WaveFormat sourceWaveFormat, int latencyMs = 25, float initialVolume = 1.0f, float masterVolume = 1.0f)
    {
        _device = device;
        _sourceWaveFormat = sourceWaveFormat;
        _latencyMs = Math.Clamp(latencyMs, 10, 100);
        _deviceVolume = initialVolume;
        _masterVolume = masterVolume;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning || _isDisposed) return;

            try
            {
                // Obter o mix format do dispositivo de destino para saber taxa de amostragem nativa
                WaveFormat targetMixFormat = _device.AudioClient.MixFormat;

                // Configurar o buffer de entrada com capacidade pequena (~120ms) para evitar atraso/drift
                _bufferedWaveProvider = new BufferedWaveProvider(_sourceWaveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(120),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true // Garante silêncio quando o buffer esvaziar, sem travar o WASAPI
                };

                // Cadeia de processamento de amostras (Float 32-bit)
                ISampleProvider sampleProvider = _bufferedWaveProvider.ToSampleProvider();

                // 1. Adaptação de canais (ex: se a fonte for 5.1/7.1 e destino for estéreo, ou mono para estéreo)
                int targetChannels = targetMixFormat.Channels > 0 ? targetMixFormat.Channels : 2;
                if (_sourceWaveFormat.Channels != targetChannels)
                {
                    sampleProvider = new ChannelAdapterSampleProvider(sampleProvider, targetChannels);
                }

                // 2. Reamostragem (Resampling dinâmico se a taxa da fonte for diferente do destino, ex: 48kHz vs 44.1kHz)
                if (sampleProvider.WaveFormat.SampleRate != targetMixFormat.SampleRate)
                {
                    sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetMixFormat.SampleRate);
                }

                // 3. Controle de Ganho/Volume individual e Master + Medição de VU
                _meteredVolume = new MeteredVolumeSampleProvider(sampleProvider)
                {
                    Volume = _deviceVolume * _masterVolume
                };

                // 4. Provedor final para o WASAPI
                IWaveProvider finalWaveProvider;
                if (targetMixFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    finalWaveProvider = _meteredVolume.ToWaveProvider();
                }
                else if (targetMixFormat.BitsPerSample == 16)
                {
                    finalWaveProvider = _meteredVolume.ToWaveProvider16();
                }
                else
                {
                    finalWaveProvider = _meteredVolume.ToWaveProvider();
                }

                // Inicializar WasapiOut em modo compartilhado com baixa latência
                _wasapiOut = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, latency: _latencyMs);
                _wasapiOut.PlaybackStopped += OnPlaybackStopped;
                _wasapiOut.Init(finalWaveProvider);
                _wasapiOut.Play();

                IsRunning = true;
            }
            catch (Exception ex)
            {
                Stop();
                throw new InvalidOperationException($"Falha ao inicializar saída de áudio para '{_device.FriendlyName}': {ex.Message}", ex);
            }
        }
    }

    public void AddSamples(byte[] buffer, int offset, int count)
    {
        if (!IsRunning || _bufferedWaveProvider == null || _isDisposed) return;

        try
        {
            _bufferedWaveProvider.AddSamples(buffer, offset, count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding samples to {DeviceFriendlyName}: {ex.Message}");
        }
    }

    public void SetVolume(float deviceVolume, float masterVolume)
    {
        _deviceVolume = deviceVolume;
        _masterVolume = masterVolume;
        if (_meteredVolume != null)
        {
            _meteredVolume.Volume = _deviceVolume * _masterVolume;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null && !_isDisposed)
        {
            System.Diagnostics.Debug.WriteLine($"Playback stopped with error on '{DeviceFriendlyName}': {e.Exception.Message}");
            PlaybackError?.Invoke(this, e.Exception);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            IsRunning = false;

            if (_wasapiOut != null)
            {
                try
                {
                    _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
                    _wasapiOut.Stop();
                    _wasapiOut.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping WasapiOut: {ex.Message}");
                }
                finally
                {
                    _wasapiOut = null;
                }
            }

            if (_bufferedWaveProvider != null)
            {
                _bufferedWaveProvider.ClearBuffer();
                _bufferedWaveProvider = null;
            }

            _meteredVolume = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        try
        {
            _device.Dispose();
        }
        catch { }
    }
}
