using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using AudioJoiner.Models;

namespace AudioJoiner.Services;

public class AudioOutputWorker : IDisposable
{
    private readonly MMDevice _device;
    private readonly WaveFormat _sourceWaveFormat;
    private readonly int _latencyMs;
    private readonly IList<EqualizerBand> _initialBands;
    private readonly bool _initialEqEnabled;

    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _bufferedWaveProvider;
    private EqualizerSampleProvider? _equalizer;
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

    public AudioOutputWorker(MMDevice device, WaveFormat sourceWaveFormat, int latencyMs = 25, float initialVolume = 1.0f, float masterVolume = 1.0f, IList<EqualizerBand>? equalizerBands = null, bool isEqEnabled = true)
    {
        _device = device;
        _sourceWaveFormat = sourceWaveFormat;
        _latencyMs = Math.Clamp(latencyMs, 10, 100);
        _deviceVolume = initialVolume;
        _masterVolume = masterVolume;
        _initialBands = equalizerBands ?? Array.Empty<EqualizerBand>();
        _initialEqEnabled = isEqEnabled;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning || _isDisposed) return;

            try
            {
                WaveFormat targetMixFormat = _device.AudioClient.MixFormat;

                _bufferedWaveProvider = new BufferedWaveProvider(_sourceWaveFormat)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(120),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };

                ISampleProvider sampleProvider = _bufferedWaveProvider.ToSampleProvider();

                // 1. Adaptação de canais (ex: 5.1/7.1 surround para estéreo ou mono para estéreo)
                int targetChannels = targetMixFormat.Channels > 0 ? targetMixFormat.Channels : 2;
                if (_sourceWaveFormat.Channels != targetChannels)
                {
                    sampleProvider = new ChannelAdapterSampleProvider(sampleProvider, targetChannels);
                }

                // 2. Reamostragem (Resampling dinâmico se a taxa da fonte for diferente do destino)
                if (sampleProvider.WaveFormat.SampleRate != targetMixFormat.SampleRate)
                {
                    sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetMixFormat.SampleRate);
                }

                // 3. DSP Equalizador Multi-Bandas em Tempo Real
                if (_initialBands.Count > 0)
                {
                    _equalizer = new EqualizerSampleProvider(sampleProvider, _initialBands)
                    {
                        IsEnabled = _initialEqEnabled
                    };
                    sampleProvider = _equalizer;
                }

                // 4. Ganho e Medidor de VU
                _meteredVolume = new MeteredVolumeSampleProvider(sampleProvider)
                {
                    Volume = _deviceVolume * _masterVolume
                };

                // 5. Provedor final para o WASAPI
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

    public void SetEqualizerEnabled(bool isEnabled)
    {
        if (_equalizer != null)
        {
            _equalizer.IsEnabled = isEnabled;
        }
    }

    public void SetEqualizerBand(int bandIndex, float gainDb)
    {
        _equalizer?.UpdateBand(bandIndex, gainDb);
    }

    public void SetEqualizerAllBands(float[] gains)
    {
        _equalizer?.UpdateAllBands(gains);
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

            _equalizer = null;
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
