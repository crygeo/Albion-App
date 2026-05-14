using AlbionApp.Domain.Interfaces;

namespace AlbionApp.Infrastructure.Services;

/// <summary>
/// Implementación de <see cref="IAlbionImageService"/> con caché de disco y throttling HTTP.
///
/// Ciclo de resolución para cada petición:
///   1. Hit de disco  → lee el PNG cacheado y retorna bytes al instante.
///   2. Miss de disco → adquiere el semáforo (máx. <see cref="MaxConcurrentDownloads"/>
///                      descargas paralelas), descarga, guarda en disco, libera semáforo.
///   3. Error HTTP 404 / cancellation → retorna null sin guardar nada en disco.
///
/// Thread-safety:
///   <see cref="GetImageBytesAsync"/> puede llamarse concurrentemente desde múltiples
///   hilos (Task.Run de imagen por imagen). El semáforo limita la presión sobre la CDN.
///   La escritura a disco no usa lock explícito: en el peor caso dos tareas descargan
///   el mismo ítem y una sobreescribe a la otra con bytes idénticos — sin corrupción.
///
/// HttpClient:
///   Se crea una única instancia por servicio (patrón recomendado). No usar
///   <c>using</c> — <see cref="AlbionImageService"/> vive como singleton en la app.
/// </summary>
public sealed class AlbionImageService : IAlbionImageService, IDisposable
{
    // ─── Configuración ────────────────────────────────────────────────────────

    private const string RenderBaseUrlItems        = "https://render.albiononline.com/v1/item/";
    private const string RenderBaseUrlDestiny        = "https://render.albiononline.com/v1/destiny/";
    private const int    MaxConcurrentDownloads = 8;

    // ─── Infraestructura ──────────────────────────────────────────────────────

    private readonly HttpClient        _http;
    private readonly SemaphoreSlim     _semaphore = new(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private readonly string            _cacheDir;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public AlbionImageService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlbionApp",
            "ImageCache");

        Directory.CreateDirectory(_cacheDir);
    }

    // ─── Implementación pública ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<byte[]?> GetImageBytesAsync(
        string itemId,
        AlbionRenderType renderType = AlbionRenderType.Item,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        var cacheFolder = Path.Combine(
            _cacheDir,
            renderType.ToString());

        Directory.CreateDirectory(cacheFolder);

        var cachePath = Path.Combine(
            cacheFolder,
            SanitizeFileName(itemId) + ".png");

        // 1. Cache hit
        if (File.Exists(cachePath))
        {
            try
            {
                return await File
                    .ReadAllBytesAsync(cachePath, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // continuar a descarga
            }
        }

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Double-check
            if (File.Exists(cachePath))
                return await File.ReadAllBytesAsync(cachePath, ct)
                    .ConfigureAwait(false);

            var bytes = await DownloadAsync(itemId, renderType, ct)
                .ConfigureAwait(false);

            if (bytes is { Length: > 0 })
                await SaveToCacheAsync(cachePath, bytes, ct)
                    .ConfigureAwait(false);

            return bytes;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _semaphore.Dispose();
    }

    // ─── Helpers privados ─────────────────────────────────────────────────────

    private async Task<byte[]?> DownloadAsync(
        string itemId,
        AlbionRenderType renderType,
        CancellationToken ct)
    {
        try
        {
            var url = GetRenderUrl(itemId, renderType);

            return await _http
                .GetByteArrayAsync(url, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.BadRequest)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveToCacheAsync(string path, byte[] bytes, CancellationToken ct)
    {
        try
        {
            // Escribir a fichero temporal y luego renombrar para evitar
            // que un crash deje un PNG truncado en caché
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Fallo al guardar → la próxima sesión descargará de nuevo, sin problema
        }
    }

    /// <summary>
    /// Elimina caracteres inválidos del itemId para usarlo como nombre de archivo.
    /// Albion IDs típicos: "T4_MAIN_AXE@2" — el @ es inválido en Windows.
    /// </summary>
    private static string SanitizeFileName(string itemId)
        => string.Concat(itemId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    
    private static string GetRenderUrl(string id, AlbionRenderType type)
    {
        var baseUrl = type switch
        {
            AlbionRenderType.Item    => RenderBaseUrlItems,
            AlbionRenderType.Destiny => RenderBaseUrlDestiny,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        return baseUrl + id;
    }
}

