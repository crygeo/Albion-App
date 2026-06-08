using LibEvents.Entities;

namespace Albion_App._5Events;

/// <summary>
/// Wrapper estático e inmutable de <see cref="EventCombatSnapshotEntry"/> — una fila
/// de jugador en el panel de barras de <see cref="EventDamageReportV"/>.
/// Los datos de un snapshot ya finalizado no cambian, así que todo se calcula
/// una sola vez en el constructor (sin Update(), sin timers, sin PropertyChanged).
/// Nombres de propiedad y formato calcados de <see cref="DpsEntryVm"/> para reusar
/// el mismo DataTemplate de barras.
/// </summary>
public sealed class SnapshotEntryDisplayVm
{
    private static readonly string[] Medals = ["🥇", "🥈", "🥉"];

    public string Username          { get; }
    public string RankLabel         { get; }
    public string DpsDisplay        { get; }
    public string DamageDisplay     { get; }
    public string HpsDisplay        { get; }
    public string HealDisplay       { get; }
    public string CombatTimeDisplay { get; }
    public double DamagePercent     { get; }
    public double HealPercent       { get; }
    public bool   HasDamage         { get; }
    public bool   HasHeal           { get; }

    public SnapshotEntryDisplayVm(EventCombatSnapshotEntry entry, int rank, long maxDamage, long maxHeal)
    {
        Username  = entry.Username;
        RankLabel = rank <= Medals.Length ? Medals[rank - 1] : rank.ToString();
        HasDamage = entry.Damage > 0;
        HasHeal   = entry.Heal   > 0;

        DpsDisplay    = HasDamage ? Fmt(entry.Dps)    : "—";
        DamageDisplay = HasDamage ? Fmt(entry.Damage) : "—";
        HpsDisplay    = HasHeal   ? Fmt(entry.Hps)    : "";
        HealDisplay   = HasHeal   ? Fmt(entry.Heal)   : "";

        CombatTimeDisplay = TimeSpan.FromSeconds(entry.CombatTimeSeconds).ToString(@"mm\:ss");

        DamagePercent = maxDamage > 0 ? (double)entry.Damage / maxDamage : 0;
        HealPercent   = maxHeal   > 0 ? (double)entry.Heal   / maxHeal   : 0;
    }

    private static string Fmt(double v) => v >= 1000 ? $"{v / 1000:0.#}K"   : $"{v:0}";
    private static string Fmt(long   v) => v >= 1000 ? $"{v / 1000.0:0.#}K" : $"{v}";
}
