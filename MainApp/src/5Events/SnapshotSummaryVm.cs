using LibEvents.Entities;

namespace Albion_App._5Events;

/// <summary>
/// Wrapper inmutable de <see cref="EventCombatSnapshot"/> para una fila de la
/// lista de rondas en <see cref="EventDamageReportV"/>.
/// </summary>
public sealed class SnapshotSummaryVm(EventCombatSnapshot snapshot, int roundNumber)
{
    public EventCombatSnapshot Snapshot    { get; } = snapshot;
    public int                 RoundNumber { get; } = roundNumber;

    public string Title    => $"Ronda {RoundNumber}";
    public string Subtitle => $"{Snapshot.StartedAt.ToLocalTime():HH:mm} · {Snapshot.TakenAt - Snapshot.StartedAt:mm\\:ss}";
}
