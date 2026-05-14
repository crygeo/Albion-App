using System.Windows;
using System.Windows.Media.Imaging;
using Albion_App.Helpers;
using AlbionApp.Application.UseCases.Player;
using AlbionApp.Domain.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App.Components.Achievement;

public partial class AchievementVm : ObservableObject
{
    private readonly PlayerAchievement _hit;
    private readonly ILocalizationService _localization;
    private readonly ProcessPlayerUseCase _player;

    public string DisplayName => _localization.GetText(_hit.NameLocalization ?? _hit.Id);
    
    [ObservableProperty] private BitmapSource? _image;
    public byte[]? ImageBytes { get; private set; }


    public short Level => _hit.Level;

    public AchievementVm(PlayerAchievement hit, ILocalizationService localization, ProcessPlayerUseCase player)
    {
        _hit = hit;
        _localization = localization;
        _player = player;

        _localization.LanguageChanged += OnChanged;
        _player.AchievementsUpdated += OnChanged;

        Refresh();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Level));
    }


    public async Task LoadImageAsync(IAlbionImageService imageService, CancellationToken ct)
    {
        try
        {
            var bytes = await imageService.GetImageBytesAsync(_hit.Id, AlbionRenderType.Destiny, ct).ConfigureAwait(false);
            ImageBytes = bytes;

            if (bytes is not { Length: > 0 } || ct.IsCancellationRequested) return;

            var bitmap = bytes.DecodeToBitmapSource();

            await Application.Current.Dispatcher.InvokeAsync(() => Image = bitmap);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Error inesperado: dejar Image=null (placeholder visible).
        }
    }
}