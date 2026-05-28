using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Utilidades.Dialogs;

namespace Albion_App._4Groups.Export;

public partial class ExportDialogVm : ObservableObject
{
    private readonly BitmapSource _bitmap;
    private readonly string       _groupName;

    public BitmapSource Preview => _bitmap;

    public ExportDialogVm(BitmapSource bitmap, string groupName)
    {
        _bitmap    = bitmap;
        _groupName = groupName;
    }

    [RelayCommand]
    private void Copy()
    {
        Clipboard.SetImage(_bitmap);
        DialogService.Instance.MensajeQueue.Enqueue("📋 Imagen copiada al portapapeles.");
    }

    [RelayCommand]
    private void SavePng()
    {
        var dlg = new SaveFileDialog
        {
            Title      = "Guardar imagen del grupo",
            FileName   = SanitizeName(_groupName) + ".png",
            DefaultExt = ".png",
            Filter     = "PNG Image|*.png",
        };

        if (dlg.ShowDialog() != true) return;

        using var stream = dlg.OpenFile();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
        encoder.Save(stream);

        DialogService.Instance.MensajeQueue.Enqueue("💾 Imagen guardada.");
    }

    private static string SanitizeName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
