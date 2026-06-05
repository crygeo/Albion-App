using System.IO;
using System.Text.Json;
using Albion_App.Models;

namespace Albion_App.Infrastructure;

/// <summary>
/// Persiste el estado del workspace en <c>%APPDATA%\AlbionApp\workspace.json</c>.
/// Patrón idéntico a AppConfigRepository: System.Text.Json + SnakeCaseLower.
/// </summary>
public sealed class WorkspacePersistenceService : IWorkspacePersistence
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AlbionApp",
        "workspace.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task SaveAsync(CalculatorWorkspaceState state)
    {
        // Síncrono intencionalmente: el handler de Window.Closed es async void y
        // WPF destruye el Dispatcher al cerrarse la ventana principal antes de que
        // cualquier await pueda completar su continuación.
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(state, Opts);
        File.WriteAllText(FilePath, json);
        return Task.CompletedTask;
    }

    public async Task<CalculatorWorkspaceState?> LoadAsync()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(FilePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CalculatorWorkspaceState>(json, Opts);
        }
        catch { return null; } // archivo corrupto → workspace limpio
    }
}
