using System.Reflection;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Discord;
using LibServices.AppConfig;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;

namespace Albion_App._0Config;

public sealed partial class ConfiguracionSvm : ObservableObject, ISectionIcons
{
    private readonly ILocalizationService _languageService;
    private readonly AppConfigService     _appConfigService;
    private readonly DiscordBotService    _discordBot;
    private readonly DiscordConfigVm      _discordConfigVm;

    // ─── Navegación ───────────────────────────────────────────────────────────

    [ObservableProperty] private string _header = "Configuracion";
    [ObservableProperty] private PackIconKind _icon = PackIconKind.AboutCircle;

    // ─── Discord: resumen (solo lectura) ──────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscordStatusText))]
    private bool _discordConnected;

    public string DiscordStatusText  => DiscordConnected ? "Bot conectado ✓" : _discordBot.StatusText;
    public string DiscordGuildName   => _appConfigService.DiscordGuildName;
    public string DiscordChannelName => _appConfigService.DiscordChannelName;

    // ─── Versión ──────────────────────────────────────────────────────────────

    public string AppVersion { get; } =
        "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—");

    // ─── Idioma ───────────────────────────────────────────────────────────────

    [ObservableProperty] private SupportedLanguage _selectedLanguage;
    public IReadOnlyList<SupportedLanguage> AvailableLanguages => _languageService.AvailableLanguages;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public ConfiguracionSvm(
        ILocalizationService languageService,
        AppConfigService     appConfigService,
        DiscordBotService    discordBot,
        DiscordConfigVm      discordConfigVm)
    {
        _languageService  = languageService;
        _appConfigService = appConfigService;
        _discordBot       = discordBot;
        _discordConfigVm  = discordConfigVm;

        _languageService.SetLanguage(SupportedLanguage.Find(appConfigService.Language));
        _selectedLanguage = _languageService.CurrentSupportedLanguage;

        _discordBot.StatusChanged += () =>
        {
            DiscordConnected = _discordBot.IsConnected;
            OnPropertyChanged(nameof(DiscordStatusText));
        };

        _discordConfigVm.Saved += () =>
        {
            OnPropertyChanged(nameof(DiscordGuildName));
            OnPropertyChanged(nameof(DiscordChannelName));
        };
    }

    // ─── Idioma ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task SaveConfiguration()
    {
        _languageService.SetLanguage(SelectedLanguage);
        _appConfigService.SetLanguage(SelectedLanguage.Code);
        return Task.CompletedTask;
    }

    // ─── Discord ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenDiscordConfig()
    {
        _discordConfigVm.Load();
        await DialogService.Instance.MostrarDialogo<DiscordConfigDialogV>(
            _discordConfigVm,
            "Discord Bot",
            DialogDefaults.Main,
            DialogDefaults.DiscordConfig);
    }
}
