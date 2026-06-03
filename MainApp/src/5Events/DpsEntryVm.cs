using CommunityToolkit.Mvvm.ComponentModel;
using LibEvents.Entities;
using System.ComponentModel;

namespace Albion_App._5Events;

/// <summary>
/// Wrapper estable de Player para una fila del DPS meter.
/// Vive mientras el Player está en la Party — no se recrea en cada tick.
///
/// Player.PropertyChanged → actualiza IsOffline inmediatamente.
/// DpsMeterVm llama Update() cuando cambian rankings o DPS rates.
/// </summary>
public sealed partial class DpsEntryVm : ObservableObject
{
    public Player Player { get; }

    // Forwarding directo desde Player (sin copia)
    public string Username => Player.Username;

    [ObservableProperty] private int     _rank;
    [ObservableProperty] private string  _rankLabel        = "👤";
    [ObservableProperty] private string  _dpsDisplay       = "—";
    [ObservableProperty] private string  _damageDisplay    = "—";
    [ObservableProperty] private string  _hpsDisplay       = "";
    [ObservableProperty] private string  _healDisplay      = "";
    [ObservableProperty] private string  _combatTimeDisplay = "00:00";
    [ObservableProperty] private double  _damagePercent;
    [ObservableProperty] private double  _healPercent;
    [ObservableProperty] private bool    _hasData;
    [ObservableProperty] private bool    _isLocalPlayer;
    [ObservableProperty] private bool    _isOffline;

    internal DpsEntryVm(Player player)
    {
        Player    = player;
        IsOffline = !player.IsLoadedOnMap;
        player.PropertyChanged += OnPlayerChanged;
    }

    internal void Detach() => Player.PropertyChanged -= OnPlayerChanged;

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Player.IsLoadedOnMap))
            IsOffline = !Player.IsLoadedOnMap;

        if (e.PropertyName == nameof(Player.Username))
            OnPropertyChanged(nameof(Username));
    }

    /// <summary>
    /// Llamado por DpsMeterVm cuando cambian rankings, máximos del grupo o DPS rates.
    /// </summary>
    internal void Update(
        int     rank,
        string  rankLabel,
        long    maxDmg,
        long    maxHeal,
        DateTime now,
        bool    isLocal)
    {
        Rank          = rank;
        RankLabel     = rankLabel;
        IsLocalPlayer = isLocal;
        HasData       = Player.Damage > 0 || Player.Heal > 0;

        DpsDisplay        = Player.Damage > 0 ? Fmt(Player.GetDps(now))  : "—";
        DamageDisplay     = Player.Damage > 0 ? Fmt(Player.Damage)       : "—";
        HpsDisplay        = Player.Heal   > 0 ? Fmt(Player.GetHps(now))  : "";
        HealDisplay       = Player.Heal   > 0 ? Fmt(Player.Heal)         : "";
        CombatTimeDisplay = HasData       ? Player.GetCombatTime(now).ToString(@"mm\:ss")
                          : IsOffline     ? "--:--"
                          :                 "00:00";

        DamagePercent = maxDmg  > 0 ? (double) Player.Damage / maxDmg  : 0;
        HealPercent   = maxHeal > 0 ? (double) Player.Heal   / maxHeal : 0;
    }

    private static string Fmt(double v) => v >= 1000 ? $"{v / 1000:0.#}K" : $"{v:0}";
    private static string Fmt(long   v) => v >= 1000 ? $"{v / 1000.0:0.#}K" : $"{v}";
}
