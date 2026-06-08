using System.Collections.ObjectModel;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibAlbionGame.Combat;
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
    private readonly CombatSession?    _combatSession;

    /// <summary>
    /// Se dispara cuando cambia el evento activo.
    /// null = ningún evento InProgress/Paused.
    /// </summary>
    public event Action<int?>? ActiveEventChanged;

    // ── ISectionIcons ─────────────────────────────────────────────────────────

    [ObservableProperty] private string       _header = "Eventos";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.CalendarStar;

    // ── Listas ────────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<GuildEvent> _activeEvents   = [];
    [ObservableProperty] private ObservableCollection<GuildEvent> _templateEvents = [];

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
    [NotifyPropertyChangedFor(nameof(IsPreparation))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(ElapsedDisplay))]
    [NotifyPropertyChangedFor(nameof(CompositionDisplay))]
    [NotifyPropertyChangedFor(nameof(TimeUntilLabel))]
    [NotifyPropertyChangedFor(nameof(Participants))]
    [NotifyPropertyChangedFor(nameof(ParticipationSummary))]
    [NotifyPropertyChangedFor(nameof(IsPublished))]
    [NotifyPropertyChangedFor(nameof(ShowDpsMeterButton))]
    private GuildEvent? _selectedEvent;

    public bool DetailIsActive   => SelectedEvent?.Status is EventStatus.Preparation
                                                           or EventStatus.Running
                                                           or EventStatus.Paused
                                                           or EventStatus.Finished;
    public bool DetailIsTemplate => SelectedEvent?.Status == EventStatus.Draft;

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

    [ObservableProperty] private DateTime? _activationDate        = DateTime.UtcNow.Date;
    [ObservableProperty] private DateTime? _activationTime        = DateTime.Today.AddHours(20);
    [ObservableProperty] private string?   _activationDescription;

    public event Action? ActivationConfirmed;
    public event Action? ActivationCancelled;

    // ── Editor de plantilla ───────────────────────────────────────────────────

    public EventEditorVm        Editor       { get; }
    public EventHistoryVm       History      { get; }
    public DpsMeterVm?          DpsMeter     { get; }
    public EventDamageReportVm  DamageReport { get; }

    /// <summary>true cuando hay combate activo — muestra el botón "Ver Daño" del medidor en vivo.</summary>
    public bool ShowDpsMeterButton => DpsMeter is not null && (IsRunning || IsPaused);

    // ── Constructor ───────────────────────────────────────────────────────────

    public EventsVm(
        BuildService         buildService,
        EventEditorVm        editor,
        DiscordBotService    discordBot,
        AppConfigService     appConfig,
        EventHistoryVm       history,
        EventDamageReportVm  damageReport,
        CombatSession?       combatSession = null,
        DpsMeterVm?          dpsMeter      = null)
    {
        _buildService    = buildService;
        _discordBot      = discordBot;
        _appConfig       = appConfig;
        _combatSession   = combatSession;
        Editor           = editor;
        History          = history;
        DpsMeter         = dpsMeter;       // instancia compartida desde App.xaml.cs
        DamageReport     = damageReport;   // instancia compartida desde App.xaml.cs

        Editor.Saved += OnEditorSaved;
        _discordBot.ParticipationChanged += OnParticipationChanged;
    }

    // ── Carga ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        var all = await _buildService.GetEventsAsync();
        ActiveEvents   = new ObservableCollection<GuildEvent>(all.Where(e =>
            e.Status == EventStatus.Preparation
         || e.Status == EventStatus.Running
         || e.Status == EventStatus.Paused
         || e.Status == EventStatus.Finished));
        TemplateEvents = new ObservableCollection<GuildEvent>(all.Where(e =>
            e.Status == EventStatus.Draft));
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
        var confirmDialog = new ConfirmDialog
        {
            TextHeader           = "Eliminar plantilla",
            Message              = $"¿Eliminar la plantilla \"{ev.Name}\"?",
            AceptarCommand       = new AsyncRelayCommand(async () =>
            {
                await _buildService.DeleteEventAsync(ev.Id);
                ActiveEvents.Remove(ev);
                TemplateEvents.Remove(ev);
                DialogService.Instance.MensajeQueue.Enqueue($"🗑️ Plantilla \"{ev.Name}\" eliminada.");
                if (ReferenceEquals(SelectedEvent, ev))
                {
                    SelectedEvent = null;
                    RightPanel    = RightPanelState.None;
                }
            }),
            DialogNameIdentifier = DialogDefaults.Confirm,
            DialogOpenIdentifier = DialogDefaults.Main,
        };
        await DialogService.Instance.MostrarDialogo(confirmDialog);
    }

    [RelayCommand]
    private async Task CloneEvent(GuildEvent ev)
    {
        var clone = new GuildEvent
        {
            Name         = $"Copia de {ev.Name}",
            Description  = ev.Description,
            Status       = EventStatus.Draft,
            BuildGroupId = ev.BuildGroupId,
        };
        await _buildService.CreateEventAsync(clone);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SelectEvent(GuildEvent ev)
    {
        // Refrescar el grupo desde la BD para que los cambios recientes al grupo
        // se reflejen en la vista del evento (evita datos de grupo desactualizados).
        if (ev.BuildGroupId.HasValue)
        {
            var freshGroups = await _buildService.GetGroupsAsync();
            ev.BuildGroup = freshGroups.FirstOrDefault(g => g.Id == ev.BuildGroupId.Value);
        }
        SelectedEvent = ev;
        RightPanel    = RightPanelState.Detail;
    }

    [RelayCommand]
    private async Task OpenHistory()
    {
        var dialog = new EventHistoryDialogV(History)
        {
            DialogOpenIdentifier = DialogDefaults.Main,
            DialogNameIdentifier = DialogDefaults.EventHistory,
        };
        await DialogService.Instance.MostrarDialogo(dialog);
    }

    [RelayCommand]
    private async Task OpenDpsMeter()
    {
        if (DpsMeter is null) return;
        var dialog = new DpsMeterDialogV(DpsMeter)
        {
            DialogOpenIdentifier = DialogDefaults.Main,
            DialogNameIdentifier = DialogDefaults.DpsMeter,
        };
        await DialogService.Instance.MostrarDialogo(dialog);
    }

    [RelayCommand]
    private async Task OpenDamageReport(GuildEvent ev)
    {
        await DamageReport.LoadAsync(ev);
        await DialogService.Instance.MostrarDialogo<EventDamageReportDialogV>(
            DamageReport,
            "Reporte de daño",
            DialogDefaults.Main,
            DialogDefaults.DamageReport);
    }

    // ── Comandos: activación ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task BeginActivate(GuildEvent ev)
    {
        SelectedEvent          = ev;
        ActivationDate         = DateTime.UtcNow.Date;
        ActivationTime         = DateTime.Today.AddHours(20);
        ActivationDescription  = ev.Description;
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

        var date        = (ActivationDate ?? DateTime.UtcNow.Date).Date;
        var timeOfDay   = ActivationTime?.TimeOfDay ?? TimeSpan.FromHours(20);
        var scheduledAt = DateTime.SpecifyKind(date.Add(timeOfDay), DateTimeKind.Utc);

        // Clonar la plantilla y activar el clon; la plantilla original queda en Draft.
        var clone = new GuildEvent
        {
            Name         = SelectedEvent.Name,
            Description  = ActivationDescription,
            Status       = EventStatus.Draft,
            BuildGroupId = SelectedEvent.BuildGroupId,
        };
        var created = await _buildService.CreateEventAsync(clone);
        await _buildService.ActivateEventAsync(created.Id, scheduledAt);
        await LoadAsync();

        ActivationConfirmed?.Invoke();
        SelectedEvent = ActiveEvents.FirstOrDefault(e => e.Id == created.Id);
        RightPanel    = RightPanelState.Detail;

        await TryAutoPublishAsync(SelectedEvent);
    }

    // ── Comandos: gestión de evento activo ────────────────────────────────────

    [RelayCommand]
    private async Task CompleteEvent(GuildEvent ev)
    {
        await TryUnpublishAsync(ev);
        ev.Status = EventStatus.Closed;
        await _buildService.UpdateEventAsync(ev);
        await LoadAsync();
        RightPanel = RightPanelState.None;
    }

    [RelayCommand]
    private async Task CancelEvent(GuildEvent ev)
    {
        var confirmDialog = new ConfirmDialog
        {
            TextHeader           = "Cancelar evento",
            Message              = $"¿Cancelar \"{ev.Name}\"? El evento pasará al historial.",
            AceptarCommand       = new AsyncRelayCommand(async () =>
            {
                await TryUnpublishAsync(ev);
                await _buildService.CancelEventAsync(ev.Id);
                await LoadAsync();
                RightPanel = RightPanelState.None;
            }),
            DialogNameIdentifier = DialogDefaults.Confirm,
            DialogOpenIdentifier = DialogDefaults.Main,
        };
        await DialogService.Instance.MostrarDialogo(confirmDialog);
    }

    [RelayCommand]
    private async Task CloseEvent(GuildEvent ev)
    {
        await TryUnpublishAsync(ev);
        await _buildService.CloseEventAsync(ev.Id);
        await LoadAsync();
        RightPanel = RightPanelState.None;
    }

    [RelayCommand]
    private async Task ClosePreparation(GuildEvent ev)
    {
        // InProgress comienza → resetear DPS y arrancar captura
        _combatSession?.Start();
        DpsMeter?.StartRefresh();
        ActiveEventChanged?.Invoke(ev.Id);

        await _buildService.ClosePreparationAsync(ev.Id);
        await LoadAsync();
        SelectedEvent = ActiveEvents.FirstOrDefault(e => e.Id == ev.Id);
        OnPropertyChanged(nameof(ShowDpsMeterButton));

        if (SelectedEvent is not null && SelectedEvent.IsPublished && _discordBot.IsConnected)
            await _discordBot.UpdateEmbedToRunningAsync(SelectedEvent);
    }

    [RelayCommand]
    private async Task PauseEvent(GuildEvent ev)
    {
        StopElapsedTimer();
        _combatSession?.Pause();
        DpsMeter?.StopRefresh();

        await _buildService.PauseEventAsync(ev.Id);
        await LoadAsync();
        SelectedEvent = ActiveEvents.FirstOrDefault(e => e.Id == ev.Id);
        OnPropertyChanged(nameof(ShowDpsMeterButton));
    }

    [RelayCommand]
    private async Task ResumeEvent(GuildEvent ev)
    {
        _combatSession?.Resume();
        DpsMeter?.StartRefresh();

        await _buildService.ResumeEventAsync(ev.Id);
        await LoadAsync();
        SelectedEvent = ActiveEvents.FirstOrDefault(e => e.Id == ev.Id);
        StartElapsedTimer();
        OnPropertyChanged(nameof(ShowDpsMeterButton));
    }

    [RelayCommand]
    private async Task EndInProgress(GuildEvent ev)
    {
        StopElapsedTimer();
        _combatSession?.Pause();
        DpsMeter?.StopRefresh();   // congela el display con datos finales
        ActiveEventChanged?.Invoke(null);

        await _buildService.EndInProgressAsync(ev.Id);
        await LoadAsync();
        SelectedEvent = ActiveEvents.FirstOrDefault(e => e.Id == ev.Id);
        OnPropertyChanged(nameof(ShowDpsMeterButton));

        if (SelectedEvent is not null && SelectedEvent.IsPublished && _discordBot.IsConnected)
            await _discordBot.UpdateEmbedToFinishedAsync(SelectedEvent);
    }

    private async Task TryAutoPublishAsync(GuildEvent? ev)
    {
        if (ev is null || !_discordBot.IsConnected) return;

        if (!ulong.TryParse(_appConfig.DiscordChannelId, out var channelId))
        {
            DialogService.Instance.MensajeQueue.Enqueue(
                "⚠️ Evento activado, pero no hay canal de Discord configurado.");
            return;
        }

        try
        {
            await _discordBot.PublishEventAsync(ev, channelId);
            OnPropertyChanged(nameof(IsPublished));
            DialogService.Instance.MensajeQueue.Enqueue(
                $"✅ Evento \"{ev.Name}\" publicado en Discord.");
        }
        catch (Exception ex)
        {
            DialogService.Instance.MensajeQueue.Enqueue(
                $"⚠️ No se pudo publicar en Discord: {ex.Message}");
        }
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
        DialogService.Instance.MensajeQueue.Enqueue($"✅ Evento \"{ev.Name}\" publicado en Discord.");
    }

    [RelayCommand]
    private async Task UnpublishFromDiscord(GuildEvent ev)
    {
        await _discordBot.UnpublishEventAsync(ev);
        OnPropertyChanged(nameof(IsPublished));
        DialogService.Instance.MensajeQueue.Enqueue($"🗑️ Evento \"{ev.Name}\" eliminado de Discord.");
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

    // ── Estado de la máquina — visibilidad por sub-estado ────────────────────

    public bool IsPreparation => SelectedEvent?.Status == EventStatus.Preparation;
    public bool IsRunning     => SelectedEvent?.Status == EventStatus.Running;
    public bool IsPaused      => SelectedEvent?.Status == EventStatus.Paused;
    public bool IsFinished    => SelectedEvent?.Status == EventStatus.Finished;

    // ── Cronómetro ────────────────────────────────────────────────────────────

    private System.Windows.Threading.DispatcherTimer? _elapsedTimer;

    /// <summary>
    /// Tiempo transcurrido formateado "hh:mm:ss".
    /// Running: ElapsedBeforePause + (UtcNow - InProgressStartedAt).
    /// Paused/Finished: solo ElapsedBeforePause (congelado).
    /// </summary>
    public string ElapsedDisplay
    {
        get
        {
            if (SelectedEvent is not { } ev) return "00:00:00";
            var elapsed = ev.ElapsedBeforePause;
            if (ev.InProgressStartedAt is { } start)
                elapsed += DateTime.UtcNow - start;
            return elapsed.ToString(@"hh\:mm\:ss");
        }
    }

    partial void OnSelectedEventChanged(GuildEvent? value)
    {
        if (value?.Status == EventStatus.Running)
            StartElapsedTimer();
        else
            StopElapsedTimer();
    }

    private void StartElapsedTimer()
    {
        if (_elapsedTimer is null)
        {
            _elapsedTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _elapsedTimer.Tick += (_, _) => OnPropertyChanged(nameof(ElapsedDisplay));
        }
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer()
    {
        _elapsedTimer?.Stop();
    }

    // ── Enum de estado del panel ──────────────────────────────────────────────

    public enum RightPanelState { None, Detail }
}
