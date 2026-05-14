namespace Albion_App.Helpers;

using System.IO;
using System.Windows.Media.Imaging;

public static class BitmapSourceExtensions
{
    /// <summary>
    /// Convierte un BitmapSource a Stream (PNG).
    /// </summary>
    public static MemoryStream ToStream(
        this BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var stream = new MemoryStream();

        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(
            BitmapFrame.Create(bitmap));

        encoder.Save(stream);

        stream.Position = 0;

        return stream;
    }
}