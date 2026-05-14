namespace AlbionApp.Application.UseCases.Player;

public sealed class PlayerAchievement
{
    public string Id { get; init; } = null!;
    public string? NameLocalization { get; init; }

    public short Level { get; set; }
}
    