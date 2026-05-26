using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;

namespace Albion_App._4Groups;

/// <summary>
/// Fila editable del editor de grupos.
/// <see cref="AvailableBuilds"/> se actualiza dinámicamente para excluir
/// builds ya seleccionados en otras filas (evita duplicados).
/// </summary>
public partial class GroupSlotVm : ObservableObject
{
    private readonly IReadOnlyList<Build> _allBuilds;

    [ObservableProperty]
    private IReadOnlyList<Build> _availableBuilds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private Build? _build;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string _emoji = "";

    [ObservableProperty] private int  _quantity     = 1;

    public string DisplayLabel =>
        Build is null ? "— Sin build —"
        : string.IsNullOrWhiteSpace(Emoji) ? Build.Name
        : $"{Emoji}  {Build.Name}";

    public GroupSlotVm(IReadOnlyList<Build> allBuilds, Build? build = null, int quantity = 1, string emoji = "")
    {
        _allBuilds       = allBuilds;
        _availableBuilds = allBuilds;
        _build           = build;
        _quantity        = quantity;
        _emoji           = emoji;
    }

    /// <summary>
    /// Actualiza la lista de builds disponibles excluyendo los ya tomados en otras filas.
    /// La build actual de este slot siempre se incluye.
    /// </summary>
    public void UpdateAvailableBuilds(IEnumerable<Build?> taken)
    {
        var takenSet = taken.ToHashSet();
        AvailableBuilds = _allBuilds
            .Where(b => !takenSet.Contains(b) || b == Build)
            .ToList();
    }

    [RelayCommand]
    private void Increment() { if (Quantity < 99) Quantity++; }

    [RelayCommand]
    private void Decrement() { if (Quantity > 1) Quantity--; }
}
