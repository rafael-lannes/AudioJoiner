namespace AudioJoiner.Models;

public class EqualizerPreset
{
    public string Name { get; set; } = string.Empty;
    public float[] Gains { get; set; } = Array.Empty<float>();

    public EqualizerPreset() { }

    public EqualizerPreset(string name, float[] gains)
    {
        Name = name;
        Gains = gains;
    }

    public static List<EqualizerPreset> GetDefaultPresets()
    {
        return new List<EqualizerPreset>
        {
            new("Flat (Padrão)", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            new("Bass Boost (Graves)", new float[] { 6.0f, 5.0f, 4.0f, 2.0f, 0, 0, 0, 0, 0, 0 }),
            new("Treble Boost (Agudos)", new float[] { 0, 0, 0, 0, 0, 1.0f, 3.0f, 5.0f, 6.5f, 7.5f }),
            new("Rock", new float[] { 4.5f, 3.5f, 1.5f, 0, -1.0f, 0, 2.0f, 3.5f, 4.5f, 4.5f }),
            new("Pop", new float[] { -1.5f, 1.0f, 3.0f, 4.0f, 3.0f, 0, -1.0f, -1.0f, 1.5f, 2.5f }),
            new("Vocal / Podcast", new float[] { -4.0f, -2.0f, 0, 2.5f, 4.0f, 4.0f, 3.0f, 1.5f, 0, -2.0f }),
            new("Cinema / Filmes", new float[] { 5.0f, 4.0f, 2.0f, 0, -1.0f, 0, 2.0f, 3.5f, 4.5f, 3.5f }),
            new("Gamer / FPS", new float[] { 4.0f, 2.0f, -2.0f, -3.0f, 1.0f, 3.0f, 4.5f, 5.5f, 4.0f, 2.0f }),
            new("Eletrônica / Dance", new float[] { 6.0f, 5.5f, 2.5f, 0, -2.0f, 1.5f, 2.5f, 4.5f, 5.5f, 4.5f }),
            new("Personalizado", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })
        };
    }
}
