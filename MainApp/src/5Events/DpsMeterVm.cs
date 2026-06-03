using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibAlbionGame.Combat;
using LibEvents.Entities;

namespace Albion_App._5Events;

/// <summary>
/// ViewModel del DPS meter en vivo — arquitectura event-driven.
///
/// Flujo:
///   Party.Players.CollectionChanged → AddEntry / RemoveEntry
///   Player.PropertyChanged          → RecalculateAll() (daño/heal/objectId)
///   Timer 500ms (solo Running)      → RefreshRateDisplays() (DPS/HPS/tiempo)
///
/// Sin polling ni reconstrucción de colección. DpsEntryVm es estable
/// durante toda la vida del Player en la Party.
/// </summary>
public partial class DpsMeterVm : ObservableObject
{
    private static readonly string[] Medals = ["🥇", "🥈", "🥉"];

    private readonly CombatSession                   _session;
    private readonly Party                           _party;
    private readonly Dictionary<Player, DpsEntryVm> _entryMap = new();
    private DispatcherTimer?                         _timer;
    private bool                                     _pendingRecalculate;

    [ObservableProperty] private ObservableCollection<DpsEntryVm> _entries = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReset))]
    private bool _hasData;

    // ── Estado ────────────────────────────────────────────────────────────────

    public bool   IsIdle    => _session.State == CombatSessionState.Idle;
    public bool   IsRunning => _session.IsRunning;
    public bool   IsPaused  => _session.IsPaused;
    public bool   IsActive  => !IsIdle;
    public bool   ShowReset => IsActive || HasData;

    public string StatusLabel => _session.State switch
    {
        CombatSessionState.Running => "ACTIVO",
        CombatSessionState.Paused  => "PAUSADO",
        _                          => "IDLE"
    };

    // ── Jugador local ─────────────────────────────────────────────────────────

    private string? _localPlayerName;

    public string? LocalPlayerName
    {
        get => _localPlayerName;
        set
        {
            _localPlayerName = value;
            RecalculateAll();
        }
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    public event Action<CombatSnapshot>? SnapshotTaken;

    // ── Constructor ───────────────────────────────────────────────────────────

    public DpsMeterVm(CombatSession session, Party party)
    {
        _session = session;
        _party   = party;

        ((INotifyCollectionChanged)_party.Players).CollectionChanged += OnPlayersCollectionChanged;

        // Registrar players que ya existen en la party
        foreach (var p in _party.Players)
            AddEntry(p);
    }

    // ── Gestión de entradas ───────────────────────────────────────────────────

    private void OnPlayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // CollectionChanged puede venir del hilo de red → despachar al UI thread
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (e.NewItems is not null)
                foreach (Player p in e.NewItems)
                    AddEntry(p);

            if (e.OldItems is not null)
                foreach (Player p in e.OldItems)
                    RemoveEntry(p);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var entry in _entryMap.Values) entry.Detach();
                _entryMap.Clear();
                Entries.Clear();
            }

            RecalculateAll();
        });
    }

    private void AddEntry(Player player)
    {
        if (_entryMap.ContainsKey(player)) return;

        var entry = new DpsEntryVm(player);
        _entryMap[player] = entry;
        Entries.Add(entry);
        player.PropertyChanged += OnPlayerPropertyChanged;
    }

    private void RemoveEntry(Player player)
    {
        player.PropertyChanged -= OnPlayerPropertyChanged;
        if (!_entryMap.TryGetValue(player, out var entry)) return;

        entry.Detach();
        _entryMap.Remove(player);
        Entries.Remove(entry);
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
            nameof(Player.Damage)     or
            nameof(Player.Heal)       or
            nameof(Player.ObjectId)   or
            nameof(Player.IsInCombat)))
            return;

        if (_pendingRecalculate) return;

        _pendingRecalculate = true;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _pendingRecalculate = false;
            RecalculateAll();
        });
    }

    // ── Recálculo ─────────────────────────────────────────────────────────────

    private void RecalculateAll()
    {
        if (Entries.Count == 0 && _party.Players.Count == 0)
        {
            HasData = false;
            return;
        }

        var now = DateTime.UtcNow;

        var withStats = Entries
            .Where(e => e.Player.Damage > 0 || e.Player.Heal > 0)
            .OrderByDescending(e => e.Player.Damage)
            .ToList();

        long maxDmg  = withStats.Count > 0 ? withStats.Max(e => e.Player.Damage) : 0;
        long maxHeal = withStats.Count > 0 ? withStats.Max(e => e.Player.Heal)   : 0;

        // Actualizar entradas con stats
        for (int i = 0; i < withStats.Count; i++)
        {
            var entry   = withStats[i];
            var isLocal = IsLocal(entry.Player);
            var label   = i < Medals.Length ? Medals[i] : $"{i + 1}";
            entry.Update(i + 1, label, maxDmg, maxHeal, now, isLocal);
        }

        // Actualizar entradas sin stats
        foreach (var entry in Entries.Where(e => e.Player.Damage == 0 && e.Player.Heal == 0))
        {
            var isLocal   = IsLocal(entry.Player);
            var isOffline = !entry.Player.IsLoadedOnMap;
            var label     = isLocal ? "👤" : (isOffline ? "⏳" : "👤");
            entry.Update(0, label, 0, 0, now, isLocal);
        }

        // Reordenar la colección: con stats primero (por daño), luego sin stats
        var desired = withStats
            .Concat(Entries.Where(e => e.Player.Damage == 0 && e.Player.Heal == 0)
                           .OrderByDescending(e => IsLocal(e.Player)))
            .ToList();

        for (int i = 0; i < desired.Count; i++)
        {
            int cur = Entries.IndexOf(desired[i]);
            if (cur != i && cur >= 0) Entries.Move(cur, i);
        }

        HasData = Entries.Count > 0;
    }

    /// <summary>
    /// Actualiza solo DPS/HPS/CombatTime (time-based). Llamado por el timer.
    /// No reordena ni recalcula porcentajes — es más ligero.
    /// </summary>
    private void RefreshRateDisplays()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in Entries.Where(e => e.HasData || e.Player.IsInCombat))
        {
            entry.Update(entry.Rank, entry.RankLabel,
                         (long)(entry.DamagePercent > 0 ? entry.Player.Damage / entry.DamagePercent : 0),
                         (long)(entry.HealPercent   > 0 ? entry.Player.Heal   / entry.HealPercent   : 0),
                         now, entry.IsLocalPlayer);
        }
    }

    private bool IsLocal(Player p)
        => LocalPlayerName is not null &&
           p.Username.Equals(LocalPlayerName, StringComparison.OrdinalIgnoreCase);

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        _party.ResetCombatStats();
        _session.Start();
        StartTimer();
        NotifyState();
        RecalculateAll();
    }
    private bool CanStart() => IsIdle;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _session.Pause();
        StopTimer();
        NotifyState();
    }
    private bool CanPause() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        _session.Resume();
        StartTimer();
        NotifyState();
    }
    private bool CanResume() => IsPaused;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        StopTimer();
        var snapshot = _session.TakeSnapshot(_party.Players);
        NotifyState();
        RecalculateAll();
        SnapshotTaken?.Invoke(snapshot);
    }
    private bool CanStop() => IsRunning || IsPaused;

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        StopTimer();
        _party.ResetCombatStats();
        _session.Reset();
        RecalculateAll();
        HasData = false;
        NotifyState();
    }
    private bool CanReset() => IsActive || HasData;

    // ── API pública ───────────────────────────────────────────────────────────

    public void StartRefresh() { StartTimer(); NotifyState(); }
    public void StopRefresh()  { StopTimer();  NotifyState(); }

    // ── Timer (solo para DPS rates) ───────────────────────────────────────────

    private void StartTimer()
    {
        if (_timer is null)
        {
            _timer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => RefreshRateDisplays();
        }
        _timer.Start();
    }

    private void StopTimer() => _timer?.Stop();

    // ── Notificaciones de estado ──────────────────────────────────────────────

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ShowReset));
        OnPropertyChanged(nameof(StatusLabel));

        StartCommand .NotifyCanExecuteChanged();
        PauseCommand .NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand  .NotifyCanExecuteChanged();
        ResetCommand .NotifyCanExecuteChanged();
    }
}
