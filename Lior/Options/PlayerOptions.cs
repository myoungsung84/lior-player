namespace Lior.Options;

public sealed class PlayerOptions
{
    public const string SectionName = "Player";

    public int DefaultVolume { get; set; } = 70;

    public bool RememberLastPosition { get; set; } = true;
}
