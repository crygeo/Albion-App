using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AlbionApp.Application.Interfaces;

namespace Albion_App.Infrastructure;

public class ClipboardService : IClipboardService
{
    public void SetImage(byte[] imageBytes)
    {
        if (imageBytes is not { Length: > 0 } bytes)
            return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            // ── Crear Bitmap para compatibilidad clásica ──

            using var bitmapStream =
                new MemoryStream(bytes);

            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = bitmapStream;
            bitmap.EndInit();
            bitmap.Freeze();

            var data = new DataObject();

            // Compatibilidad con Paint / apps Win32
            data.SetImage(bitmap);

            // ── PNG real con alpha ──
            // IMPORTANTE: no usar using aquí

            var pngStream =
                new MemoryStream(bytes);

            data.SetData(
                "PNG",
                pngStream);

            Clipboard.SetDataObject(
                data,
                true);
        });
    }

    public void SetText(string text)
    {
        Clipboard.SetText(text);
    }
}