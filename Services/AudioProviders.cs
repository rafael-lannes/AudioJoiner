using NAudio.Wave;

namespace AudioJoiner.Services;

/// <summary>
/// Provedor de amostras que monitora o nível de pico (VU Meter) e aplica volume.
/// </summary>
public class MeteredVolumeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _volume = 1.0f;
    private float _peak = 0f;
    private readonly object _lock = new();

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 2.0f);
    }

    public float CurrentPeak
    {
        get
        {
            lock (_lock)
            {
                float p = _peak;
                // Decaimento suave do pico
                _peak *= 0.85f;
                if (_peak < 0.001f) _peak = 0f;
                return p;
            }
        }
    }

    public MeteredVolumeSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead == 0)
        {
            return 0;
        }

        float vol = _volume;
        float maxSample = 0f;

        for (int i = 0; i < samplesRead; i++)
        {
            buffer[offset + i] *= vol;
            float abs = Math.Abs(buffer[offset + i]);
            if (abs > maxSample)
            {
                maxSample = abs;
            }
        }

        lock (_lock)
        {
            if (maxSample > _peak)
            {
                _peak = maxSample;
            }
        }

        return samplesRead;
    }
}

/// <summary>
/// Adaptador que converte fontes multicanais (ex: 5.1/7.1 surround) ou mono para estéreo (2 canais),
/// ou replica canais se necessário.
/// </summary>
public class ChannelAdapterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private readonly int _targetChannels;
    private readonly WaveFormat _waveFormat;
    private float[] _sourceBuffer = Array.Empty<float>();

    public WaveFormat WaveFormat => _waveFormat;

    public ChannelAdapterSampleProvider(ISampleProvider source, int targetChannels = 2)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        _targetChannels = targetChannels;
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, targetChannels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_sourceChannels == _targetChannels)
        {
            return _source.Read(buffer, offset, count);
        }

        // Calcula quantos frames são solicitados
        int requestedFrames = count / _targetChannels;
        int neededSourceSamples = requestedFrames * _sourceChannels;

        if (_sourceBuffer.Length < neededSourceSamples)
        {
            _sourceBuffer = new float[neededSourceSamples];
        }

        int sourceSamplesRead = _source.Read(_sourceBuffer, 0, neededSourceSamples);
        int framesRead = sourceSamplesRead / _sourceChannels;

        if (_sourceChannels > 2 && _targetChannels == 2)
        {
            // Downmix de multicanal (ex: 5.1 / 7.1) para Estéreo
            // Canais típicos 5.1: 0=FL, 1=FR, 2=C, 3=LFE, 4=BL, 5=BR
            for (int f = 0; f < framesRead; f++)
            {
                int srcIdx = f * _sourceChannels;
                int dstIdx = offset + (f * 2);

                float fl = _sourceBuffer[srcIdx];
                float fr = _sourceBuffer[srcIdx + 1];
                float center = _sourceChannels > 2 ? _sourceBuffer[srcIdx + 2] * 0.707f : 0f;
                float lfe = _sourceChannels > 3 ? _sourceBuffer[srcIdx + 3] * 0.5f : 0f;
                float sl = _sourceChannels > 4 ? _sourceBuffer[srcIdx + 4] * 0.707f : 0f;
                float sr = _sourceChannels > 5 ? _sourceBuffer[srcIdx + 5] * 0.707f : 0f;

                buffer[dstIdx] = fl + center + lfe + sl;
                buffer[dstIdx + 1] = fr + center + lfe + sr;
            }
        }
        else if (_sourceChannels == 1 && _targetChannels == 2)
        {
            // Mono para Estéreo
            for (int f = 0; f < framesRead; f++)
            {
                float mono = _sourceBuffer[f];
                int dstIdx = offset + (f * 2);
                buffer[dstIdx] = mono;
                buffer[dstIdx + 1] = mono;
            }
        }
        else if (_sourceChannels == 2 && _targetChannels == 1)
        {
            // Estéreo para Mono
            for (int f = 0; f < framesRead; f++)
            {
                int srcIdx = f * 2;
                buffer[offset + f] = (_sourceBuffer[srcIdx] + _sourceBuffer[srcIdx + 1]) * 0.5f;
            }
        }
        else
        {
            // Fallback genérico: copia os canais disponíveis ou zera
            for (int f = 0; f < framesRead; f++)
            {
                int srcIdx = f * _sourceChannels;
                int dstIdx = offset + (f * _targetChannels);
                for (int ch = 0; ch < _targetChannels; ch++)
                {
                    buffer[dstIdx + ch] = ch < _sourceChannels ? _sourceBuffer[srcIdx + ch] : 0f;
                }
            }
        }

        return framesRead * _targetChannels;
    }
}
