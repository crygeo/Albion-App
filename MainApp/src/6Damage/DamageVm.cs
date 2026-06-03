using System.Windows;
using Albion_App._5Events;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using LibAlbionGame.Combat;
using LibAlbionGame.Trackers;
using LibEvents.Entities;
using LibEvents.Services;
using MaterialDesignThemes.Wpf;

namespace Albion_App._6Damage;

/// <summary>
/// Sección Damage — muestra el jugador local y el DPS meter en vivo.
///
/// Responsabilidades:
///   • Expone el componente <see cref="DpsMeterVm"/> con sus propios botones.
///   • Muestra nombre/guild del jugador local cuando el paquete JoinResponse llega.
///   • Persiste snapshots en la BD cuando el GL pulsa Detener en el componente.
///   • Recibe <see cref="SetActiveEvent"/> desde App.xaml.cs para saber en qué
///     evento guardar el snapshot.
/// </summary>
public partial class DamageVm : ObservableObject, ISectionIcons
{
    private readonly BuildService  _buildService;
    private int?                   _activeEventId;
    // ── ISectionIcons ─────────────────────────────────────────────────────────

    [ObservableProperty] private string       _header = "Damage";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.SwordCross;

    // ── Jugador local (poblado cuando llega JoinResponse) ─────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayer))]
    private string _playerName = "";

    [ObservableProperty] private string _guildName    = "";
    [ObservableProperty] private string _allianceName = "";

    public bool HasPlayer => !string.IsNullOrEmpty(PlayerName);

    // ── Componente DPS ────────────────────────────────────────────────────────

    public DpsMeterVm DpsMeter { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public DamageVm(
        CombatSession  session,
        PartyTracker   partyTracker,
        BuildService   buildService,
        DpsMeterVm     dpsMeter)
    {
        _buildService = buildService;
        DpsMeter      = dpsMeter;

        // Escuchar al jugador local desde el hilo de red → despachar al UI thread
        partyTracker.LocalPlayerUpdated += p =>
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlayerName               = p.Username;
                GuildName                = p.GuildName;
                AllianceName             = p.AllianceName;
                DpsMeter.LocalPlayerName = p.Username;
            });

        // Persistir snapshot cuando el GL pulsa Detener en el componente
        DpsMeter.SnapshotTaken += OnSnapshotTaken;

    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Llamado desde App.xaml.cs cuando un evento pasa a InProgress o cierra.
    /// Permite que el snapshot se asocie al evento correcto.
    /// </summary>
    public void SetActiveEvent(int? eventId) => _activeEventId = eventId;

    // ── Snapshot ──────────────────────────────────────────────────────────────

    private void OnSnapshotTaken(CombatSnapshot snapshot)
    {
        if (_activeEventId is not { } eventId) return;

        var entries = snapshot.Entries.Select(e => new EventCombatSnapshotEntry
        {
            Username          = e.Username,
            Damage            = e.Damage,
            Heal              = e.Heal,
            TakenDamage       = e.TakenDamage,
            Overhealed        = e.Overhealed,
            Dps               = e.Dps,
            Hps               = e.Hps,
            CombatTimeSeconds = e.CombatTime.TotalSeconds,
        });

        _ = _buildService.SaveSnapshotAsync(eventId, snapshot.StartedAt, snapshot.TakenAt, entries);
    }
}
