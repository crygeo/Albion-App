using Albion_App.Models;

namespace Albion_App.Infrastructure;

/// <summary>Contrato para guardar y restaurar el estado del workspace de la calculadora.</summary>
public interface IWorkspacePersistence
{
    Task SaveAsync(CalculatorWorkspaceState state);
    Task<CalculatorWorkspaceState?> LoadAsync();
}
