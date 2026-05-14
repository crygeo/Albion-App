using System.Collections.ObjectModel;
using Albion_App.Dialog;
using Albion_App.Features.DataStatic;
using Albion_App.Models;
using Albion_App.ViewModel;
using AlbionApp.Domain.Crafting;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.ItemSearch;
using AlbionApp.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Utilidades.Dialogs;
using ItemBaseVm = Albion_App.Components.Item.ItemBaseVm;
using ItemSearchVM = Albion_App.Dialog.ItemSearchVM;

namespace Albion_App._1Calculadora;

/// <summary>
/// ViewModel de la página Calculadora de Crafteo.
///
/// Organizado en 5 regiones que corresponden a las 4 cards de la UI más
/// la metadata de navegación (icono + header para el sidebar).
///
/// Decisión de diseño: VM único con regiones claras en lugar de sub-VMs
/// porque todo el estado es cohesivo (el cambio en cualquier card puede
/// afectar a las demás). Si la complejidad crece, el paso natural es
/// extraer RecipeCarouselVM y CraftingResultVM como sub-VMs observables.
///
/// Política de imágenes:
/// - El ítem seleccionado retorna su <see cref="ItemBaseVm"/> directamente
///   desde el diálogo de búsqueda (imagen ya cargada, sin re-descarga).
/// - Los ingredientes de la receta son ítems distintos; se construyen nuevos
///   <see cref="ItemBaseVm"/> y se lanza <c>LoadImageAsync</c> fire-and-forget
///   bajo un <see cref="CancellationTokenSource"/> que se cancela cuando el
///   ítem objetivo cambia, evitando descargas huérfanas.
///
/// TODO (deuda técnica conocida):
/// - Integrar IMarketPriceService para precios reales en Card 4.
/// - Integrar LibAlbionProtocol para cargar inventario en Card 3.
/// - Exponer IReadOnlyList en lugar de ObservableCollection en propiedades
///   públicas si no se requiere mutación desde la vista.
/// </summary>
public sealed partial class CalculadoraSvm : ObservableObject, ISectionIcons
{
    // ─── Dependencias ─────────────────────────────────────────────────────────

    private readonly ItemSearchVM   _itemSearchVm;
    private readonly IItemDataService _itemDataService;

    // ═══════════════════════════════════════════════════════════════════════════
    // REGIÓN 0 — Metadata de navegación (sidebar)
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty] private string _header = "Calculadora";
    [ObservableProperty] private PackIconKind _icon = PackIconKind.Calculator;

    // ═══════════════════════════════════════════════════════════════════════════
    // REGIÓN 1 — Card 1: Ítem objetivo
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(RecipeCarouselLabel))]

    private ItemBaseVm? _selectedItem;

    public bool HasSelectedItem => SelectedItem is not null;

    /// <summary>
    /// Precio aproximado del ítem seleccionado en plata.
    /// Placeholder hasta integrar IMarketPriceService.
    /// </summary>
    public decimal ApproxPrice => 1_500_000m;

    // ═══════════════════════════════════════════════════════════════════════════
    // REGIÓN 2 — Card 2: Carrusel de recetas + configuración de crafteo
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Carrusel ──────────────────────────────────────────────────────────────

    public ObservableCollection<RecipeVm> RecipeOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentRecipe))]
    [NotifyPropertyChangedFor(nameof(PreviousRecipeM))]
    [NotifyPropertyChangedFor(nameof(NextRecipeM))]
    [NotifyPropertyChangedFor(nameof(HasPreviousRecipe))]
    [NotifyPropertyChangedFor(nameof(HasNextRecipe))]
    [NotifyPropertyChangedFor(nameof(RecipeCarouselLabel))]
    private int _recipeIndex;

    /// <summary>
    /// True cuando hay recetas.
    /// </summary>
    public bool HasRecipes => RecipeOptions.Count > 0;

    /// <summary>True cuando hay más de una receta; ambas flechas se habilitan.</summary>
    public bool HasMultipleRecipes => RecipeOptions.Count > 1;

    /// <summary>
    /// Ítem seleccionado pero sin receta disponible (no crafteable o no en catálogo).
    /// Usado para mostrar el placeholder correcto en el carrusel.
    /// </summary>
    public bool HasSelectedItemWithoutRecipe => HasSelectedItem && !HasRecipes;

    public RecipeVm? CurrentRecipe  => RecipeOptions.Count > 0 ? RecipeOptions[RecipeIndex] : null;

    /// <summary>
    /// Receta "anterior" con wrap-around: si estamos en la primera, expone la última
    /// para que el peek izquierdo muestre la receta del extremo opuesto.
    /// </summary>
    public RecipeVm? PreviousRecipeM => RecipeOptions.Count > 1
        ? RecipeOptions[RecipeIndex > 0 ? RecipeIndex - 1 : RecipeOptions.Count - 1]
        : null;

    /// <summary>
    /// Receta "siguiente" con wrap-around: si estamos en la última, expone la primera.
    /// </summary>
    public RecipeVm? NextRecipeM => RecipeOptions.Count > 1
        ? RecipeOptions[RecipeIndex < RecipeOptions.Count - 1 ? RecipeIndex + 1 : 0]
        : null;

    // Con scroll infinito los peeks son visibles siempre que haya > 1 receta.
    public bool HasPreviousRecipe => HasMultipleRecipes;
    public bool HasNextRecipe     => HasMultipleRecipes;

    /// <summary>Etiqueta de posición: "Receta (2/5)".</summary>
    public string RecipeCarouselLabel => RecipeOptions.Count > 0
        ? $"Receta ({RecipeIndex + 1}/{RecipeOptions.Count})"
        : "—";

    // ── Ciudad y parámetros ───────────────────────────────────────────────────

    public IReadOnlyList<CraftingCity> AvailableCities { get; } =
        Enum.GetValues<CraftingCity>()
            .Where(c => c != CraftingCity.None)
            .ToArray();

    [ObservableProperty] private CraftingCity _selectedCity = CraftingCity.Bridgewatch;

    public IReadOnlyList<JournalBonusOptionM> JournalBonusOptions { get; } =
    [
        new(0.00m, "0% — Sin bono de diario"),
        new(0.10m, "10% — Diario de rendimiento"),
        new(0.20m, "20% — Diario premium"),
    ];

    [ObservableProperty] private JournalBonusOptionM? _selectedJournalBonus;
    [ObservableProperty] private bool _usePremium;
    [ObservableProperty] private bool _useFocus;

    // ═══════════════════════════════════════════════════════════════════════════
    // REGIÓN 3 — Card 3: Cantidades e inventario
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIngredientInventory))]
    private int _quantityToCraft = 1;

    public ObservableCollection<IngredientInventoryM> IngredientInventory { get; } = [];
    public bool HasIngredientInventory => IngredientInventory.Count > 0;

    // ═══════════════════════════════════════════════════════════════════════════
    // REGIÓN 4 — Card 4: Resumen de la operación
    // ═══════════════════════════════════════════════════════════════════════════

    public ObservableCollection<MaterialResultM> MaterialResults { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasResults))]
    private decimal _totalCost;

    public bool HasResults => MaterialResults.Count > 0;

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public CalculadoraSvm(
        ItemSearchVM    itemSearchVm,
        IItemDataService itemDataService)
    {
        _itemSearchVm    = itemSearchVm;
        _itemDataService = itemDataService;

        SelectedJournalBonus = JournalBonusOptions[0];

        // Suscripción a CollectionChanged: garantiza que los bindings de Visibility
        // que dependen de HasRecipes / HasMultipleRecipes se actualicen en el momento
        // exacto en que la colección cambia, sin depender de llamadas manuales dispersas.
        RecipeOptions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasRecipes));
            OnPropertyChanged(nameof(HasMultipleRecipes));
            OnPropertyChanged(nameof(HasSelectedItemWithoutRecipe));
            // CurrentRecipe es computed desde RecipeOptions[RecipeIndex]. Cuando
            // RecipeIndex no cambia (ya era 0) la propiedad no se notifica sola,
            // así que debemos hacerlo aquí cada vez que la colección muta.
            OnPropertyChanged(nameof(CurrentRecipe));
            OnPropertyChanged(nameof(PreviousRecipeM));
            OnPropertyChanged(nameof(NextRecipeM));
            OnPropertyChanged(nameof(RecipeCarouselLabel));
            // HasPreviousRecipe / HasNextRecipe dependen de HasMultipleRecipes
            // pero son propiedades independientes — deben notificarse explícitamente.
            OnPropertyChanged(nameof(HasPreviousRecipe));
            OnPropertyChanged(nameof(HasNextRecipe));

            PreviousRecipeCommand.NotifyCanExecuteChanged();
            NextRecipeCommand.NotifyCanExecuteChanged();
        };

        // Mismo patrón para las colecciones dependientes de Cards 3 y 4.
        IngredientInventory.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasIngredientInventory));
            CalcularCommand.NotifyCanExecuteChanged();
        };

        MaterialResults.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasResults));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COMANDOS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Abre el diálogo de búsqueda de ítems.</summary>
    [RelayCommand]
    private async Task BuscarItemAsync()
    {

        var dialog = new ItemSearchDialogV(_itemSearchVm)
        {
            DialogOpenIdentifier = DialogDefaults.Main,
            AceptarCommand = new AsyncRelayCommand<ItemBaseVm>(OnItemSeleccionadoAsync),
            CancelarCommand = new AsyncRelayCommand(() => Task.CompletedTask)
        };

        await DialogService.Instance.MostrarDialogo(dialog);
    }

    /// <summary>Limpia el ítem seleccionado y resetea todo el estado de la calculadora.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void LimpiarItem()
    {
        CancelarCargaDeIngredientes();
        SelectedItem = null;      // → OnSelectedItemChanged → HasSelectedItemWithoutRecipe
        RecipeOptions.Clear();    // → CollectionChanged → HasRecipes, HasMultipleRecipes, HasSelectedItemWithoutRecipe
        RecipeIndex = 0;
        IngredientInventory.Clear(); // → CollectionChanged → HasIngredientInventory
        MaterialResults.Clear();     // → CollectionChanged → HasResults
        TotalCost = 0;
    }

    /// <summary>
    /// Navega a la receta anterior con scroll infinito:
    /// primera → última, resto → anterior.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasMultipleRecipes))]
    private void PreviousRecipe()
    {
        RecipeIndex = RecipeIndex > 0 ? RecipeIndex - 1 : RecipeOptions.Count - 1;
        RefreshCarouselDependencies();
    }

    /// <summary>
    /// Navega a la receta siguiente con scroll infinito:
    /// última → primera, resto → siguiente.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasMultipleRecipes))]
    private void NextRecipe()
    {
        RecipeIndex = RecipeIndex < RecipeOptions.Count - 1 ? RecipeIndex + 1 : 0;
        RefreshCarouselDependencies();
    }

    /// <summary>
    /// Carga el inventario de recursos desde el protocolo del juego.
    /// TODO: integrar con LibAlbionProtocol → IPlayerInventoryService.
    /// </summary>
    [RelayCommand]
    private void CargarRecursos()
    {
        // Placeholder: en la implementación real se invocaría
        // IPlayerInventoryService.GetCurrentInventory() y se mapearían
        // los ownedCounts en IngredientInventory.
    }

    /// <summary>Ejecuta el cálculo de crafteo y popula la Card 4.</summary>
    [RelayCommand(CanExecute = nameof(HasIngredientInventory))]
    private void Calcular()
    {
        // TODO: integrar con CraftingCalculatorService.Calculate(CraftingParameters).
        // Por ahora popula con datos placeholder para demostrar el layout.
        PopularResultadosPlaceholder();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HANDLERS DE CAMBIO (CommunityToolkit partial methods)
    // ═══════════════════════════════════════════════════════════════════════════

    partial void OnSelectedItemChanged(ItemBaseVm? value)
    {
        // HasSelectedItemWithoutRecipe depende de SelectedItem; se notifica aquí
        // para que el placeholder del carrusel reaccione de forma inmediata.
        OnPropertyChanged(nameof(HasSelectedItemWithoutRecipe));
    }

    partial void OnQuantityToCraftChanged(int value)
    {
        // Recalcular RequiredCount de cada ingrediente cuando cambia la cantidad objetivo.
        RebuildIngredientInventory();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MÉTODOS PRIVADOS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CancellationTokenSource para las cargas de imagen de los ingredientes.
    /// Se cancela cada vez que cambia el ítem objetivo, evitando descargas huérfanas.
    /// </summary>
    private CancellationTokenSource? _ingredientCts;

    private async Task OnItemSeleccionadoAsync(ItemBaseVm? item)
    {
        if (item is null)
            return;

        // El item ya tiene imagen cargada — no re-descargamos.
        SelectedItem = item;
        CargarRecetasDeItem(SelectedItem);
        LimpiarItemCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Construye las opciones del carrusel a partir del catálogo de crafteo.
    /// Un ítem puede tener cero, una o varias recetas (crafteo base + mejoras de encantamiento).
    /// El carrusel refleja todas las recetas disponibles con scroll infinito.
    ///
    /// Flujo:
    ///   1. Cancela cualquier carga de imagen de ingredientes en vuelo.
    ///   2. Limpia el estado anterior del carrusel.
    ///   3. Crea un CTS fresco para las cargas de imagen de esta tanda de ingredientes.
    ///   4. Por cada <see cref="IRecipe"/> devuelta por <see cref="ItemDataService"/>:
    ///      mapea los <see cref="IIngredient"/> a <see cref="IngredientVm"/> via
    ///      <see cref="BuildIngredientVm"/>, que lanza la imagen en fire-and-forget.
    ///   5. Notifica las dependencias del carrusel y reconstruye el inventario de ingredientes.
    /// </summary>
    private void CargarRecetasDeItem(ItemBaseVm itemVm)
    {
        CancelarCargaDeIngredientes();
        RecipeOptions.Clear();
        RecipeIndex = 0;

        var rawRecipes = _itemDataService.GetRecipes(itemVm.ItemId);

        if (rawRecipes.Length > 0)
        {
            _ingredientCts = new CancellationTokenSource();
            var ct = _ingredientCts.Token;

            for (int i = 0; i < rawRecipes.Length; i++)
            {
                var raw = rawRecipes[i];

                var ingredients = raw.Ingredients
                    .Select(ing => new IngredientVm
                    {
                        Item                 = BuildIngredientVm(ing.ItemId, ct),
                        Count                = ing.Count,
                        ParticipatesInReturn = ing.ParticipatesInReturn
                    })
                    .ToArray();

                RecipeOptions.Add(new RecipeVm
                {
                    Index         = i,
                    Label         = "Receta de Crafteo",
                    SubLabel      = raw.AmountCrafted > 1
                                        ? $"×{raw.AmountCrafted} producidos por ciclo"
                                        : "×1 producido por ciclo",
                    Ingredients   = ingredients,
                    AmountCrafted = raw.AmountCrafted
                });
            }
        }

        // RecipeOptions.Add → CollectionChanged → HasRecipes, HasMultipleRecipes, HasSelectedItemWithoutRecipe
        RefreshCarouselDependencies();
        RebuildIngredientInventory();
    }

    /// <summary>
    /// Crea un <see cref="ItemBaseVm"/> para un ingrediente delegando en
    /// <see cref="ItemSearchVM.BuildItemVm"/>, que centraliza localización,
    /// imagen y el fallback para IDs no encontrados en catálogo.
    /// </summary>
    private ItemBaseVm BuildIngredientVm(string itemId, CancellationToken ct)
        => _itemSearchVm.BuildItemVm(itemId, ct);

    /// <summary>Reconstruye IngredientInventory con los RequiredCounts actualizados.</summary>
    private void RebuildIngredientInventory()
    {
        // Preservar owned counts del usuario si el mismo ingrediente ya existía.
        var previousOwned = IngredientInventory
            .ToDictionary(i => i.ItemId, i => i.OwnedCount);

        IngredientInventory.Clear(); // → CollectionChanged → HasIngredientInventory + CalcularCommand

        if (CurrentRecipe is null) return;

        int qty = Math.Max((int)1, (int)QuantityToCraft);

        foreach (var ingredient in CurrentRecipe.Ingredients)
        {
            var row = new IngredientInventoryM
            {
                Item = ingredient.Item,
                RequiredCount = ingredient.Count * qty
            };

            if (previousOwned.TryGetValue(ingredient.ItemId, out var owned))
                row.OwnedCount = owned;

            IngredientInventory.Add(row); // → CollectionChanged
        }
        // La última notificación la dispara el último Add via CollectionChanged.
    }

    /// <summary>
    /// Notifica los comandos del carrusel para que WPF actualice el estado
    /// de los botones ← y →. Se llama siempre que RecipeIndex cambia.
    /// </summary>
    private void RefreshCarouselDependencies()
    {
        OnPropertyChanged(nameof(CurrentRecipe));
        OnPropertyChanged(nameof(PreviousRecipeM));
        OnPropertyChanged(nameof(NextRecipeM));
        OnPropertyChanged(nameof(RecipeCarouselLabel));
        OnPropertyChanged(nameof(HasPreviousRecipe));
        OnPropertyChanged(nameof(HasNextRecipe));
        PreviousRecipeCommand.NotifyCanExecuteChanged();
        NextRecipeCommand.NotifyCanExecuteChanged();
        RebuildIngredientInventory();
    }

    /// <summary>
    /// Cancela cualquier carga de imagen de ingredientes en vuelo.
    /// Llamar antes de cambiar el ítem objetivo o limpiar la calculadora.
    /// </summary>
    private void CancelarCargaDeIngredientes()
    {
        _ingredientCts?.Cancel();
        _ingredientCts?.Dispose();
        _ingredientCts = null;
    }

    /// <summary>
    /// Popula MaterialResults con datos placeholder hasta integrar el servicio
    /// real de precios de mercado. Los valores son inventados solo para mostrar el layout.
    /// </summary>
    private void PopularResultadosPlaceholder()
    {
        MaterialResults.Clear();
        if (CurrentRecipe is null) return;

        decimal total = 0;

        foreach (var ingredient in CurrentRecipe.Ingredients)
        {
            var inv = IngredientInventory.FirstOrDefault(i => i.ItemId == ingredient.ItemId);
            int needed = inv?.NeededCount ?? (ingredient.Count * Math.Max((int)1, (int)QuantityToCraft));
            var precio = 100_000m; // placeholder — reemplazar con IMarketPriceService

            var row = new MaterialResultM
            {
                Item = ingredient.Item,   // Reutilizar el VM (y su imagen) del ingrediente
                NetQuantity = needed,
                BuyLocation = SelectedCity.ToString(),
                UnitPrice = precio
            };

            MaterialResults.Add(row);
            total += row.TotalCost;
        }

        TotalCost = total;
        // HasResults ya se notifica via MaterialResults.CollectionChanged (último Add).
    }
}
