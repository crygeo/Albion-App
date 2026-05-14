namespace AlbionApp.Domain.Interfaces;

/// <summary>
/// Servicio de imágenes de ítems de Albion Online.
///
/// Retorna los bytes PNG crudos en lugar de un tipo WPF para que este contrato
/// pueda vivir en Domain sin crear dependencia hacia System.Windows.
/// La conversión a <c>BitmapSource</c> es responsabilidad de la capa de presentación.
///
/// Estrategia de caché de dos niveles:
///   1. Disco  — persiste entre sesiones (AppData\AlbionApp\ImageCache\)
///   2. Red    — <c>https://render.albiononline.com/v1/item/{itemId}</c>
///
/// Las descargas concurrentes se limitan con un semáforo para no saturar la CDN
/// ni el ancho de banda del usuario cuando se muestran cientos de ítems a la vez.
/// </summary>
public interface IAlbionImageService
{
    /// <summary>
    /// Retorna los bytes PNG de la imagen del ítem, o <c>null</c> si el ítem no
    /// tiene imagen o si ocurrió un error irrecuperable.
    ///
    /// La llamada es segura de invocar desde cualquier hilo. Si la imagen ya está
    /// en caché de disco, retorna rápido (lectura de archivo). Si no, descarga en
    /// background respetando el límite de concurrencia.
    /// </summary>
    Task<byte[]?> GetImageBytesAsync(string itemId, AlbionRenderType renderType = AlbionRenderType.Item, CancellationToken ct = default);
}

public enum AlbionRenderType
{
    Item,
    Destiny
}