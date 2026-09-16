using NAudio.Dsp;
using NAudio.Wave;
using AudioJoiner.Models;

namespace AudioJoiner.Services;

/// <summary>
/// Provedor de amostras DSP que implementa um Equalizador Gráfico Multi-Bandas em tempo real
/// utilizando filtros BiQuad Peaking EQ por canal.
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly object _lock = new();

    private BiQuadFilter[][] _filters; // [canal][banda]
    private float[] _bandGains;
    private float[] _bandFrequencies;
    private float[] _bandWidths;
    private bool _isEnabled = true;
    private bool _hasActiveFilters = false;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public EqualizerSampleProvider(ISampleProvider source, IList<EqualizerBand> bands)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;

        int bandCount = bands.Count;
        _bandGains = new float[bandCount];
        _bandFrequencies = new float[bandCount];
        _bandWidths = new float[bandCount];

        for (int i = 0; i < bandCount; i++)
        {
            _bandGains[i] = bands[i].GainDb;
            _bandFrequencies[i] = bands[i].Frequency;
            _bandWidths[i] = bands[i].BandWidth;
        }

        _filters = new BiQuadFilter[_channels][];
        RebuildFilters();
    }

    public void UpdateBand(int bandIndex, float gainDb)
    {
        lock (_lock)
        {
            if (bandIndex >= 0 && bandIndex < _bandGains.Length)
            {
                _bandGains[bandIndex] = gainDb;
                RebuildFilters();
            }
        }
    }

    public void UpdateAllBands(float[] gains)
    {
        lock (_lock)
        {
            for (int i = 0; i < Math.Min(gains.Length, _bandGains.Length); i++)
            {
                _bandGains[i] = gains[i];
            }
            RebuildFilters();
        }
    }

    private void RebuildFilters()
    {
        int bandCount = _bandGains.Length;
        bool anyNonZero = false;

        for (int ch = 0; ch < _channels; ch++)
        {
            _filters[ch] = new BiQuadFilter[bandCount];
            for (int b = 0; b < bandCount; b++)
            {
                float gain = _bandGains[b];
                float freq = Math.Clamp(_bandFrequencies[b], 20f, (_sampleRate / 2f) - 100f);
                float q = _bandWidths[b] > 0 ? _bandWidths[b] : 1.0f;

                if (Math.Abs(gain) > 0.05f)
                {
                    anyNonZero = true;
                }

                _filters[ch][b] = BiQuadFilter.PeakingEQ(_sampleRate, freq, q, gain);
            }
        }

        _hasActiveFilters = anyNonZero;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead == 0 || !_isEnabled || !_hasActiveFilters)
        {
            return samplesRead;
        }

        lock (_lock)
        {
            int frames = samplesRead / _channels;
            int bandCount = _bandGains.Length;

            for (int frame = 0; frame < frames; frame++)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    int sampleIndex = offset + (frame * _channels) + ch;
                    float sample = buffer[sampleIndex];

                    var channelFilters = _filters[ch];
                    for (int b = 0; b < bandCount; b++)
                    {
                        if (Math.Abs(_bandGains[b]) > 0.05f)
                        {
                            sample = channelFilters[b].Transform(sample);
                        }
                    }

                    // Limitar suavemente (soft-clipping) para evitar distorção digital caso haja ganho excessivo
                    buffer[sampleIndex] = Math.Clamp(sample, -1.0f, 1.0f);
                }
            }
        }

        return samplesRead;
    }
}
