using Albion_App.Features.DataStatic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;
using LibEvents.Services;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

public partial class EventHistoryVm : ObservableObject
{
    private readonly BuildService _buildService;
    private List<GuildEvent> _allHistory = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredHistory))]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredHistory))]
    private DateTime? _filterFrom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredHistory))]
    private DateTime? _filterTo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryElapsedDisplay))]
    [NotifyPropertyChangedFor(nameof(HistoryCancelledInfo))]
    [NotifyPropertyChangedFor(nameof(CompositionDisplay))]
    private GuildEvent? _selectedHistoryEvent;

    public EventHistoryVm(BuildService buildService) => _buildService = buildService;

    public async Task LoadAsync()
    {
        _allHistory = await _buildService.GetEventHistoryAsync();
        SelectedHistoryEvent = null;
        OnPropertyChanged(nameof(FilteredHistory));
    }

    public IEnumerable<GuildEvent> FilteredHistory
    {
        get
        {
            var q = _allHistory.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
                q = q.Where(e => e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            if (FilterFrom.HasValue)
                q = q.Where(e => e.ScheduledAt >= FilterFrom);
            if (FilterTo.HasValue)
                q = q.Where(e => e.ScheduledAt <= FilterTo.Value.Date.AddDays(1));
            return q;
        }
    }

    [RelayCommand]
    private void SelectHistoryEvent(GuildEvent ev) => SelectedHistoryEvent = ev;

    [RelayCommand]
    private async Task DeleteHistoryEvent(GuildEvent ev)
    {
        var confirmDialog = new ConfirmDialog
        {
            TextHeader           = "Eliminar del historial",
            Message              = $"¿Eliminar \"{ev.Name}\" del historial permanentemente?",
            AceptarCommand       = new AsyncRelayCommand(async () =>
            {
                await _buildService.DeleteEventAsync(ev.Id);
                _allHistory.Remove(ev);
                OnPropertyChanged(nameof(FilteredHistory));
                DialogService.Instance.MensajeQueue.Enqueue($"🗑️ \"{ev.Name}\" eliminado del historial.");
                if (ReferenceEquals(SelectedHistoryEvent, ev))
                    SelectedHistoryEvent = null;
            }),
            DialogNameIdentifier = DialogDefaults.Confirm,
            DialogOpenIdentifier = DialogDefaults.EventHistory,
        };
        await DialogService.Instance.MostrarDialogo(confirmDialog);
    }

    // ── Solo lectura — panel de detalle ──────────────────────────────────────

    public string HistoryElapsedDisplay
    {
        get
        {
            if (SelectedHistoryEvent is not { } ev) return "";
            TimeSpan? ts = ev.Status == EventStatus.Cancelled
                ? ev.CancelledElapsedTime
                : ev.ElapsedBeforePause == TimeSpan.Zero ? null : (TimeSpan?)ev.ElapsedBeforePause;
            return ts is null ? "" : ts.Value.ToString(@"hh\:mm\:ss");
        }
    }

    public string HistoryCancelledInfo
    {
        get
        {
            if (SelectedHistoryEvent?.Status != EventStatus.Cancelled) return "";
            if (SelectedHistoryEvent.CancelledFromStatus is not { } prev) return "Cancelado";
            var label = prev switch
            {
                EventStatus.Draft        => "Borrador",
                EventStatus.Preparation  => "Preparación",
                EventStatus.Running      => "En progreso",
                EventStatus.Paused       => "Pausado",
                EventStatus.Finished     => "Finalizado",
                _                        => prev.ToString()
            };
            return $"Cancelado desde: {label}";
        }
    }

    public IEnumerable<CompositionSlotDisplayVm> CompositionDisplay
    {
        get
        {
            if (SelectedHistoryEvent is null) yield break;
            var slots = SelectedHistoryEvent.BuildGroup?.Slots.OrderBy(s => s.SortOrder)
                     ?? Enumerable.Empty<BuildGroupSlot>();
            foreach (var slot in slots)
            {
                var members = SelectedHistoryEvent.Participants
                    .Where(p => p.BuildId == slot.BuildId)
                    .Select(p => "@" + p.DiscordUsername)
                    .ToList();
                yield return new CompositionSlotDisplayVm(
                    slot.Emoji ?? "▪️",
                    slot.Build?.Name ?? "—",
                    slot.Quantity,
                    members.Count,
                    string.Join("  ·  ", members));
            }
        }
    }
}
