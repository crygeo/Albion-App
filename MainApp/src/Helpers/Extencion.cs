using System.IO;
using System.Windows.Media.Imaging;

namespace Albion_App.Helpers;

public static class Extencion
{
    public static BitmapSource DecodeToBitmapSource(this byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        // BitmapFrame.Create es thread-safe; se puede invocar fuera del hilo UI.
        // OnLoad: lee el stream completo antes de cerrarlo.
        // Freeze: hace la BitmapSource inmutable y compartible.
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
}