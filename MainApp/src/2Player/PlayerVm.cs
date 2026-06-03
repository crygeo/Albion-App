using System.Windows;
using System.Windows.Media;
using Albion_App.Components.Destinyboard;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using AlbionApp.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using LibEvents.Entities;
using CommunityToolkit.Mvvm.Input;
using LibServices.PlayerState;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;

namespace Albion_App._2Player;

public partial class PlayerVm : ObservableObject, ISectionIcons
{
    private readonly PlayerStateService _playerStateService;

    // ── Sección ───────────────────────────────────────────────────────────────

    [ObservableProperty] private string       _header = "Player";
    [ObservableProperty] private PackIconKind _icon   = PackIconKind.AccountCircle;

    // ── DestinyBoard ──────────────────────────────────────────────────────────

    [ObservableProperty] private DestinyBoardVm _destinyBoardVm;

    // ── Datos del jugador (del RSP Join) ──────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initials))]
    [NotifyPropertyChangedFor(nameof(AvatarBrush))]
    [NotifyPropertyChangedFor(nameof(PlayerVisibility))]
    [NotifyPropertyChangedFor(nameof(NoPlayerVisibility))]
    private string _playerName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuildVisibility))]
    [NotifyPropertyChangedFor(nameof(NoGuildVisibility))]
    private string _guildName = "";

    // ── Propiedades derivadas ─────────────────────────────────────────────────

    public string     Initials         => string.IsNullOrEmpty(PlayerName) ? "?" : PlayerName[0].ToString().ToUpper();
    public Visibility PlayerVisibility => string.IsNullOrEmpty(PlayerName) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NoPlayerVisibility => string.IsNullOrEmpty(PlayerName) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GuildVisibility  => string.IsNullOrEmpty(GuildName)  ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NoGuildVisibility => !string.IsNullOrEmpty(PlayerName) && string.IsNullOrEmpty(GuildName)
        ? Visibility.Visible : Visibility.Collapsed;

    public SolidColorBrush AvatarBrush
    {
        get
        {
            if (string.IsNullOrEmpty(PlayerName))
                return new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

            Color[] palette =
            [
                Color.FromRgb(0xC0, 0x39, 0x2B),
                Color.FromRgb(0xE6, 0x7E, 0x22),
                Color.FromRgb(0x27, 0xAE, 0x60),
                Color.FromRgb(0x1A, 0xBC, 0x9C),
                Color.FromRgb(0x29, 0x80, 0xB9),
                Color.FromRgb(0x8E, 0x44, 0xAD),
                Color.FromRgb(0xD3, 0x54, 0x00),
                Color.FromRgb(0x16, 0xA0, 0x85),
            ];

            var index = Math.Abs(PlayerName.GetHashCode()) % palette.Length;
            return new SolidColorBrush(palette[index]);
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public PlayerVm(DestinyBoardVm destinyBoardVm, PlayerStateService playerStateService)
    {
        _destinyBoardVm     = destinyBoardVm;
        _playerStateService = playerStateService;
    }

    // ── Update desde JoinHandler (hilo de red → UI thread) ───────────────────

    public void UpdateFromJoin(Player player)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            PlayerName = player.Username;
            GuildName  = player.GuildName;
        });
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenDialog()
    {
        await DialogService.Instance.MostrarDialogo<DestinyBoardDialogV>(
            DestinyBoardVm,
            "DestinyBoard",
            DialogDefaults.Main,
            DialogDefaults.DestinyBoard);
    }
}
