using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AlbionApp.Infrastructure.Services;
using LibEvents.Entities;

namespace Albion_App._4Groups.Export;

/// <summary>
/// Loads item images for all slots in a BuildGroup and renders the group
/// card layout to a frozen BitmapSource using an off-screen WPF control.
/// </summary>
public sealed class BuildGroupImageExporter
{
    private readonly AlbionImageService _imageService;

    public BuildGroupImageExporter(AlbionImageService imageService)
        => _imageService = imageService;

    /// <summary>
    /// Loads images for all build slots and renders the group card layout.
    /// Can be called from any thread; rendering is dispatched to the UI thread internally.
    /// </summary>
    public async Task<BitmapSource> RenderAsync(BuildGroup group, CancellationToken ct = default)
    {
        var cards = new List<BuildCardExportVm>();

        foreach (var slot in group.Slots.OrderBy(s => s.SortOrder))
        {
            var build = slot.Build;
            if (build is null) continue;
            var label = string.IsNullOrWhiteSpace(slot.Emoji)
                ? build.Name
                : $"{slot.Emoji}  {build.Name}";

            var extras = new List<BitmapImage>();
            foreach (var id in new[] { build.Extra1ItemId, build.Extra2ItemId, build.Extra3ItemId, build.Extra4ItemId })
            {
                var img = await LoadAsync(id, ct);
                if (img is not null) extras.Add(img);
            }

            cards.Add(new BuildCardExportVm
            {
                Label    = label,
                Bag      = await LoadAsync(build.BagItemId,      ct),
                Head     = await LoadAsync(build.HeadItemId,     ct),
                Cape     = await LoadAsync(build.CapeItemId,     ct),
                MainHand = await LoadAsync(build.MainHandItemId, ct),
                Chest    = await LoadAsync(build.ChestItemId,    ct),
                OffHand  = await LoadAsync(build.OffHandItemId,  ct),
                Potion   = await LoadAsync(build.PotionItemId,   ct),
                Boots    = await LoadAsync(build.BootsItemId,    ct),
                Food     = await LoadAsync(build.FoodItemId,     ct),
                Mount    = await LoadAsync(build.MountItemId,    ct),
                Extras   = extras,
            });
        }

        var exportVm = new BuildGroupExportVm
        {
            GroupName = group.Name,
            Cards     = cards,
        };

        return await Application.Current.Dispatcher.InvokeAsync(() => RenderToImage(exportVm));
    }

    private static BitmapSource RenderToImage(BuildGroupExportVm vm)
    {
        var control = new BuildGroupExportV { DataContext = vm };

        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        control.Arrange(new Rect(control.DesiredSize));
        control.UpdateLayout();

        // DesiredSize is reliable off-screen; ActualWidth/Height may remain 0.
        var width  = (int)Math.Ceiling(Math.Max(control.DesiredSize.Width,  1));
        var height = (int)Math.Ceiling(Math.Max(control.DesiredSize.Height, 1));

        // Get screen DPI from any open window; fall back to 96 if none available.
        var anyWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault();
        var source    = anyWindow is not null ? PresentationSource.FromVisual(anyWindow) : null;
        var dpiX      = source?.CompositionTarget?.TransformToDevice.M11 * 96.0 ?? 96.0;
        var dpiY      = source?.CompositionTarget?.TransformToDevice.M22 * 96.0 ?? 96.0;

        var rtb = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        rtb.Render(control);
        rtb.Freeze();
        return rtb;
    }

    private async Task<BitmapImage?> LoadAsync(string? itemId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        var bytes = await _imageService.GetImageBytesAsync(itemId, ct: ct);
        return ToBitmapImage(bytes);
    }

    private static BitmapImage? ToBitmapImage(byte[]? bytes)
    {
        if (bytes is null or { Length: 0 }) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
