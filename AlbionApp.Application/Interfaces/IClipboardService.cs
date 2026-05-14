namespace AlbionApp.Application.Interfaces;

public interface IClipboardService
{
    void SetImage(byte[] imagen);
    void SetText(string text);
    
}