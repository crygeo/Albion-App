using System.Collections.ObjectModel;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Discord;
using LibEvents.Entities;
using LibEvents.Services;
using LibServices.AppConfig;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;

namespace Albion_App._5Events;

/// <summary>Slot de composición enriquecido con participantes confirmados para la UI.</summary>
public sealed record CompositionSlotDisplayVm(
    string Emoji,
    string Name,
    int    Quantity,
    int    Confirmed,
    string UsernamesText)
{
    public string CounterText => $"{Confirmed}/{Quantity}";
}

/// <summary>
/// Panel de eventos con dos zonas:
///   • Activos  — eventos con Status=Open (countdown, composición, acciones).
///   • Plantillas — eventos Closed listos para activar.
/// </summary>
public partial class EventsVm : ObservableObject, ISectionIcons
{
    private readonly BuildService      _buildService;
    private readonly DiscordBotService _discordBot;
    private readonly AppConfigService  _appConfig;

    // ── ISectionIcons ─────────────────────────────────────────────────────────

    [ObservableProperty] private string       _header = "Eventos";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.CalendarStar;

    // ── Listas ────────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<GuildEvent> _activeEvents    = [];
    [ObservableProperty] private ObservableCollection<GuildEvent> _templateEvents  = [];

    // ── Selección y estado del panel derecho ──────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNone))]
    [NotifyPropertyChangedFor(nameof(IsDetail))]
    private RightPanelState _rightPanel = RightPanelState.None;

    public bool IsNone   => RightPanel == RightPanelState.None;
    public bool IsDetail => RightPanel == RightPanelState.Detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailIsActive))]
    [NotifyPropertyChangedFor(nameof(DetailIsTemplate))]
    [NotifyPropertyChangedFor(nameof(CompositionDisplay))]
    [NotifyPropertyChangedFor(nameof(TimeUntilLabel))]
    [NotifyPropertyChangedFor(nameof(Participants))]
    [NotifyPropertyChangedFor(nameof(ParticipationSummary))]
    [NotifyPropertyChangedFor(nameof(IsPublished))]
    private GuildEvent? _selectedEvent;

    public bool DetailIsActive   => SelectedEvent?.Status == EventStatus.Open;
    public bool DetailIsTemplate => SelectedEvent?.Status == EventStatus.Closed;

    // ── Composición con participantes del evento seleccionado ─────────────────

    public IEnumerable<CompositionSlotDisplayVm> CompositionDisplay
    {
        get
        {
            if (SelectedEvent is null) yield break;
            var slots = SelectedEvent.BuildGroup?.Slots.OrderBy(s => s.SortOrder)
                     ?? Enumerable.Empty<BuildGroupSlot>();
            foreach (var slot in slots)
            {
                var members = SelectedEvent.Participants
                    .Where(p => p.BuildId == slot.BuildId)
                    .Select(p => "@" + p.DiscordUsername)
                    .ToList();
                yield return new CompositionSlotDisplayVm(
                    slot.Emoji ?? "▪️",
                    slot.Build?.Name  ?? "—",
                    slot.Quantity,
                    members.Count,
                    string.Join("  ·  ", members));
            }
        }
    }

    /// <summary>Participantes inscritos en el evento seleccionado.</summary>
    public IEnumerable<EventParticipant> Participants =>
        SelectedEvent?.Participants ?? Enumerable.Empty<EventParticipant>();

    /// <summary>Total de inscritos sobre el total de cupos.</summary>
    public string ParticipationSummary
    {
        get
        {
            if (SelectedEvent is null) return "";
            var filled = SelectedEvent.Participants.Count;
            var total  = SelectedEvent.BuildGroup?.Slots.Sum(s => s.Quantity) ?? 0;
            return total > 0 ? $"{filled}/{total} inscritos" : $"{filled} inscritos";
        }
    }

    /// <summary>True si el evento activo está publicado en Discord.</summary>
    public bool IsPublished => SelectedEvent?.IsPublished ?? false;

    public string TimeUntilLabel
    {
        get
        {
            if (SelectedEvent?.ScheduledAt is not { } dt) return "";
            var diff = dt - DateTime.UtcNow;
            if (diff.TotalSeconds < 0)  return "En curso";
            if (diff.TotalDays   >= 1)  return $"{(int)diff.TotalDays}d {diff.Hours}h";
            if (diff.TotalHours  >= 1)  return $"{(int)diff.TotalHours}h {diff.Minutes}m";
            return $"{(int)diff.TotalMinutes}m restantes";
        }
    }

    // ── Formulario de activación ──────────────────────────────────────────────

    [ObservableProperty] private DateTime? _activationDate = DateTime.UtcNow.Date;
    [ObservableProperty] private string    _activationTime = "20:00";

    public event Action? ActivationConfirmed;
    public event Action? ActivationCancelled;

    // ── Editor de plantilla ───────────────────────────────────────────────────

    public EventEditorVm Editor { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public EventsVm(
        BuildService      buildService,
        EventEditorVm     editor,
        DiscordBotService discordBot,
        AppConfigService  appConfig)
    {
        _buildService = buildService;
        _discordBot   = discordBot;
        _appConfig    = appConfig;
        Editor        = editor;

        Editor.Saved += OnEditorSaved;
        _discordBot.ParticipationChanged += OnParticipationChanged;
    }

    // ── Carga ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        var all = await _buildService.GetEventsAsync();
        ActiveEvents   = new ObservableCollection<GuildEvent>(all.Where(e => e.Status == EventStatus.Open));
        TemplateEvents = new ObservableCollection<GuildEvent>(all.Where(e => e.Status != EventStatus.Open));
    }

    // ── Comandos: plantillas ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task NewEvent()
    {
        SelectedEvent = null;
        await Editor.LoadAsync(null);
        await DialogService.Instance.MostrarDialogo<EventEditorDialogV>(
            Editor,
            "Plantilla de evento",
            DialogDefaults.Main,
            DialogDefaults.EventEditor);
    }

    [RelayCommand]
    private async Task EditEvent(GuildEvent ev)
    {
        SelectedEvent = ev;
        await Editor.LoadAsync(ev);
        await DialogService.Instance.MostrarDialogo<EventEditorDialogV>(
            Editor,
            "Editar plantilla",
            DialogDefaults.Main,
            DialogDefaults.EventEditor);
    }

    [RelayCommand]
    private async Task DeleteEvent(GuildEvent ev)
    {
        await _buildService.DeleteEventAsync(ev.Id);
        ActiveEvents.Remove(ev);
        TemplateEvents.Remove(ev);
        if (ReferenceEquals(SelectedEvent, ev))
        {
            SelectedEvent = null;
            RightPanel    = RightPanelState.None;
        }
    }

    [RelayCommand]
    private void SelectEvent(GuildEvent ev)
    {
        SelectedEvent = ev;
        RightPanel    = RightPanelState.Detail;
    }

    // ── Comandos: activación ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task BeginActivate(GuildEvent ev)
    {
        SelectedEvent  = ev;
        ActivationDate = DateTime.UtcNow.Date;
        ActivationTime = "20:00";
        await DialogService.Instance.MostrarDialogo<ActivateEventDialogV>(
            this,
            "Activar evento",
            DialogDefaults.Main,
            DialogDefaults.ActivateEvent);
    }

    [RelayCommand]
    private void CancelActivation() => ActivationCancelled?.Invoke();

    [RelayCommand]
    private async Task ConfirmActivation()
    {
        if (SelectedEvent is null) return;

        var date = (ActivationDate ?? DateTime.UtcNow.Date).Date;
        SelectedEvent.ScheduledAt = TimeSpan.TryParseExact(ActivationTime, @"hh\:mm", null, out var ts)
            ? DateTime.SpecifyKind(date.Add(ts), DateTimeKind.Utc)
            : DateTime.SpecifyKind(date,          DateTimeKind.Utc);
        SelectedEvent.Status = EventStatus.Open;

        await _buildService.UpdateEventAsync(SelectedEvent);
        await LoadAsync();

        ActivationConfirmed?.Invoke();
        RightPanel = RightPanelState.Detail;
    }

    // ── Comandos: gestión de evento activo ────────────────────────────────────

    [RelayCommand]
    private async Task CompleteEvent(GuildEvent ev)
    {
        await TryUnpublishAsync(ev);
        ev.Status = EventStatus.Completed;
        await _buildService.UpdateEventAsync(ev);
        await LoadAsync();
        RightPanel = RightPanelState.None;
    }

    [RelayCommand]
    private async Task CancelEvent(GuildEvent ev)
    {
        await TryUnpublishAsync(ev);
        ev.Status = EventStatus.Cancelled;
        await _buildService.UpdateEventAsync(ev);
        await LoadAsync();
        RightPanel = RightPanelState.None;
    }

    [RelayCommand]
    private async Task CloseEvent(GuildEvent ev)
    {
        await TryUnpublishAsync(ev);
        ev.Status      = EventStatus.Closed;
        ev.ScheduledAt = null;
        await _buildService.UpdateEventAsync(ev);
        await LoadAsync();
        RightPanel = RightPanelState.None;
    }

    private async Task TryUnpublishAsync(GuildEvent ev)
    {
        if (!ev.IsPublished || !_discordBot.IsConnected) return;
        try { await _discordBot.UnpublishEventAsync(ev); }
        catch { /* best effort — Discord puede estar caído */ }
    }

    // ── Comandos: Discord ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PublishToDiscord(GuildEvent ev)
    {
        if (!_discordBot.IsConnected) return;
        if (!ulong.TryParse(_appConfig.DiscordChannelId, out var channelId)) return;

        await _discordBot.PublishEventAsync(ev, channelId);
        OnPropertyChanged(nameof(IsPublished));
        DialogService.Instance.MensajeQueue.Enqueue($"✅  \"{ev.Name}\" publicado en Discord.");
    }

    [RelayCommand]
    private async Task UnpublishFromDiscord(GuildEvent ev)
    {
        await _discordBot.UnpublishEventAsync(ev);
        OnPropertyChanged(nameof(IsPublished));
        DialogService.Instance.MensajeQueue.Enqueue($"🗑️  \"{ev.Name}\" eliminado de Discord.");
    }

    // ── Callback participación Discord ────────────────────────────────────────

    private async void OnParticipationChanged(int eventId)
    {
        var prevId = SelectedEvent?.Id;
        await LoadAsync();

        if (prevId == eventId)
        {
            // Reemplazar con el objeto recién cargado (tiene los participantes actualizados)
            SelectedEvent = ActiveEvents.FirstOrDefault(e => e.Id == eventId)
                         ?? TemplateEvents.FirstOrDefault(e => e.Id == eventId);
        }
    }

    // ── Callback del editor ───────────────────────────────────────────────────

    private async void OnEditorSaved()
    {
        await LoadAsync();
        SelectedEvent = null;
    }

    // ── Enum de estado del panel ──────────────────────────────────────────────

    public enum RightPanelState { None, Detail }
}
