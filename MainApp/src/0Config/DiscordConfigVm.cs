using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Discord;
using LibServices.AppConfig;

namespace Albion_App._0Config;

/// <summary>
/// ViewModel del diálogo de configuración del bot Discord.
///
/// Flujo:
///   1. Usuario ingresa token y pulsa Conectar.
///   2. El bot conecta y dispara Ready — se cargan los guilds.
///   3. Usuario selecciona guild → se cargan los canales de texto.
///   4. Usuario selecciona canal → se habilita Guardar.
///
/// Si el bot ya estaba conectado al abrir el diálogo, se cargan los guilds directamente
/// y los campos del token quedan deshabilitados (hay que desconectar primero).
/// </summary>
public sealed partial class DiscordConfigVm : ObservableObject
{
    private readonly DiscordBotService _bot;
    private readonly AppConfigService  _config;

    [ObservableProperty] private string  _token        = "";
    [ObservableProperty] private bool    _isConnecting;
    [ObservableProperty] private string? _connectError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private GuildItem? _selectedGuild;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private ChannelItem? _selectedChannel;

    [ObservableProperty]
    private DiscordInteractionMode _interactionMode = DiscordInteractionMode.Buttons;

    public ObservableCollection<GuildItem>   Guilds   { get; } = [];
    public ObservableCollection<ChannelItem> Channels { get; } = [];

    public bool IsConnected    => _bot.IsConnected;
    public bool IsDisconnected => !_bot.IsConnected;
    public bool CanConnect     => !_bot.IsConnected && !IsConnecting;

    public bool IsButtonsMode
    {
        get => InteractionMode == DiscordInteractionMode.Buttons;
        set
        {
            if (value) InteractionMode = DiscordInteractionMode.Buttons;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReactionsMode));
        }
    }

    public bool IsReactionsMode
    {
        get => InteractionMode == DiscordInteractionMode.Reactions;
        set
        {
            if (value) InteractionMode = DiscordInteractionMode.Reactions;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsButtonsMode));
        }
    }

    public event Action? Saved;
    public event Action? Cancelled;

    public DiscordConfigVm(DiscordBotService bot, AppConfigService config)
    {
        _bot    = bot;
        _config = config;
    }

    /// <summary>Resetea el estado y carga guilds si el bot ya está listo.</summary>
    public void Load()
    {
        Token         = _config.DiscordBotToken;
        InteractionMode = Enum.TryParse<DiscordInteractionMode>(
            _config.DiscordInteractionMode, out var m)
            ? m
            : DiscordInteractionMode.Buttons;
        ConnectError  = null;
        IsConnecting  = false;
        Guilds.Clear();
        Channels.Clear();
        SelectedGuild   = null;
        SelectedChannel = null;
        RefreshConnectionProps();

        if (_bot.IsReady)
            LoadGuilds();
    }

    // ── Guilds y canales ──────────────────────────────────────────────────────

    private void LoadGuilds()
    {
        Guilds.Clear();
        foreach (var (id, name) in _bot.GetGuilds().OrderBy(g => g.Name))
            Guilds.Add(new GuildItem(id, name));

        RefreshConnectionProps();
        SaveCommand.NotifyCanExecuteChanged();

        if (ulong.TryParse(_config.DiscordGuildId, out var gId))
            SelectedGuild = Guilds.FirstOrDefault(g => g.Id == gId);
    }

    partial void OnSelectedGuildChanged(GuildItem? value)
    {
        Channels.Clear();
        SelectedChannel = null;
        if (value is null) return;

        foreach (var (id, name, _) in _bot.GetTextChannels(value.Id).OrderBy(c => c.Position))
            Channels.Add(new ChannelItem(id, "#" + name));

        if (ulong.TryParse(_config.DiscordChannelId, out var cId))
            SelectedChannel = Channels.FirstOrDefault(c => c.Id == cId);
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Connect()
    {
        if (string.IsNullOrWhiteSpace(Token)) return;
        IsConnecting = true;
        ConnectError = null;
        RefreshConnectionProps();

        Action? onReady = null;
        try
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            onReady = () => tcs.TrySetResult(true);
            _bot.Ready += onReady;

            _bot.InteractionMode = InteractionMode;
            await _bot.StartAsync(Token.Trim());

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(20_000)) == tcs.Task;
            if (completed)
                LoadGuilds();
            else
                ConnectError = "El bot no respondió en 20 segundos. Verifica el token.";
        }
        catch (Exception ex)
        {
            ConnectError = ex.Message;
        }
        finally
        {
            if (onReady is not null) _bot.Ready -= onReady;
            IsConnecting = false;
            RefreshConnectionProps();
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        await _bot.StopAsync();
        Guilds.Clear();
        Channels.Clear();
        SelectedGuild   = null;
        SelectedChannel = null;
        RefreshConnectionProps();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => _bot.IsConnected && SelectedGuild is not null && SelectedChannel is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var channelName = SelectedChannel!.Name.TrimStart('#');
        _config.SetDiscordConfig(
            Token.Trim(),
            SelectedGuild!.Id.ToString(),
            SelectedChannel.Id.ToString(),
            SelectedGuild.Name,
            channelName,
            InteractionMode.ToString());
        _bot.InteractionMode = InteractionMode;
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshConnectionProps()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(CanConnect));
    }
}

public record GuildItem(ulong Id, string Name)
{
    public override string ToString() => Name;
}

public record ChannelItem(ulong Id, string Name)
{
    public override string ToString() => Name;
}
