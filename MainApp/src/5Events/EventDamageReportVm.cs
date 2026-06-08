using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LibEvents.Entities;
using LibEvents.Services;

namespace Albion_App._5Events;

/// <summary>
/// VM del diálogo "Ver Daño" para eventos finalizados — instancia única inyectada
/// desde App.xaml.cs en EventsVm (igual que EventHistoryVm/DpsMeterVm).
///
/// Carga todas las snapshots de combate guardadas durante un evento y expone
/// la ronda seleccionada como una lista de barras de daño/heal estáticas.
/// </summary>
public partial class EventDamageReportVm : ObservableObject
{
    private readonly BuildService _buildService;

    [ObservableProperty] private string _eventName = "";
    [ObservableProperty] private bool   _hasSnapshots;

    [ObservableProperty] private ObservableCollection<SnapshotSummaryVm> _snapshots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEntries))]
    private SnapshotSummaryVm? _selectedSnapshot;

    public EventDamageReportVm(BuildService buildService) => _buildService = buildService;

    /// <summary>Carga las snapshots del evento dado y selecciona la ronda más reciente.</summary>
    public async Task LoadAsync(GuildEvent ev)
    {
        EventName = ev.Name;

        var snapshots = await _buildService.GetSnapshotsAsync(ev.Id);
        Snapshots = new ObservableCollection<SnapshotSummaryVm>(
            snapshots.Select((s, i) => new SnapshotSummaryVm(s, i + 1)));

        HasSnapshots     = Snapshots.Count > 0;
        SelectedSnapshot = Snapshots.LastOrDefault();
    }

    /// <summary>Barras de daño/heal de la ronda seleccionada, con porcentajes relativos al máximo de esa ronda.</summary>
    public IEnumerable<SnapshotEntryDisplayVm> SelectedEntries
    {
        get
        {
            if (SelectedSnapshot is null) yield break;

            var entries = SelectedSnapshot.Snapshot.Entries.ToList();
            long maxDamage = entries.Count > 0 ? entries.Max(e => e.Damage) : 0;
            long maxHeal   = entries.Count > 0 ? entries.Max(e => e.Heal)   : 0;

            for (int i = 0; i < entries.Count; i++)
                yield return new SnapshotEntryDisplayVm(entries[i], i + 1, maxDamage, maxHeal);
        }
    }
}
