using Albion_App.Components.Item;
using Albion_App.Infrastructure;
using AlbionApp.Application.UseCases.SearchItems;
using AlbionApp.Domain.Interfaces;
using AlbionApp.Domain.Interfaces.Services;
using AlbionApp.Domain.ItemSearch;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;
using LibEvents.Services;
using Utilidades.Dialogs;

namespace Albion_App._3Builds;

public partial class BuildEditorVm : ObservableObject
{
    private readonly BuildService       _buildService;
    private readonly IItemSearchService _search;
    private readonly IItemDataService   _itemData;

    private Build? _editingBuild;

    // ── Metadata ──────────────────────────────────────────────────────────────

    [ObservableProperty] private string  _name        = "";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private bool    _isDirty;

    partial void OnNameChanged(string value)        => IsDirty = true;
    partial void OnDescriptionChanged(string? value) => IsDirty = true;

    // ── Browser de ítems (panel izquierdo del editor) ─────────────────────────

    public BuildItemBrowserVm Browser { get; }

    // ── Slots de equipamiento ─────────────────────────────────────────────────

    public SlotVm Head     { get; }
    public SlotVm Chest    { get; }
    public SlotVm Boots    { get; }
    public SlotVm Cape     { get; }
    public SlotVm Bag      { get; }
    public SlotVm MainHand { get; }
    public SlotVm OffHand  { get; }
    public SlotVm Potion   { get; }
    public SlotVm Food     { get; }
    public SlotVm Mount    { get; }

    // ── Slots adicionales (cualquier ítem, máx 1 c/u) ────────────────────────

    public SlotVm Extra1 { get; }
    public SlotVm Extra2 { get; }
    public SlotVm Extra3 { get; }
    public SlotVm Extra4 { get; }

    // ── Eventos ───────────────────────────────────────────────────────────────

    public event Action? Saved;
    public event Action? Cancelled;

    // ── Constructor ───────────────────────────────────────────────────────────

    public BuildEditorVm(
        BuildService        buildService,
        IItemSearchService  search,
        IItemDataService    itemData,
        SearchItemsUseCase  searchUseCase,
        ItemBaseVmFactory   itemVmFactory)
    {
        _buildService = buildService;
        _search       = search;
        _itemData     = itemData;

        Browser = new BuildItemBrowserVm(searchUseCase, itemVmFactory);

        Head     = new SlotVm("Cabeza",          SlotType.Head,     1,  search, itemData);
        Chest    = new SlotVm("Pechera",          SlotType.Armor,    1,  search, itemData);
        Boots    = new SlotVm("Botas",            SlotType.Shoes,    1,  search, itemData);
        Cape     = new SlotVm("Capa",             SlotType.Cape,     1,  search, itemData);
        Bag      = new SlotVm("Bolsa",            SlotType.Bag,      1,  search, itemData);
        MainHand = new SlotVm("Arma Principal",   SlotType.MainHand, 1,  search, itemData);
        OffHand  = new SlotVm("Arma Secundaria",  SlotType.OffHand,  1,  search, itemData);
        Potion   = new SlotVm("Poción",           SlotType.Potion,   10, search, itemData);
        Food     = new SlotVm("Comida",           SlotType.Food,     10, search, itemData);
        Mount    = new SlotVm("Montura",          SlotType.Mount,    1,  search, itemData);

        Extra1   = new SlotVm("Extra 1",          SlotType.None,     1,  search, itemData);
        Extra2   = new SlotVm("Extra 2",          SlotType.None,     1,  search, itemData);
        Extra3   = new SlotVm("Extra 3",          SlotType.None,     1,  search, itemData);
        Extra4   = new SlotVm("Extra 4",          SlotType.None,     1,  search, itemData);

        MainHand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SlotVm.Item))
                RefreshOffHandLock();
        };

        foreach (var slot in AllSlots)
            slot.PropertyChanged += (_, _) => IsDirty = true;
    }

    // ── Carga / limpieza ──────────────────────────────────────────────────────

    public void LoadBuild(Build? build)
    {
        _editingBuild = build;
        Name        = build?.Name        ?? "";
        Description = build?.Description;

        foreach (var slot in AllSlots) slot.Reset();

        if (build is null)
        {
            Browser.Initialize();
            return;
        }

        LoadSlot(Head,     build.HeadItemId);
        LoadSlot(Chest,    build.ChestItemId);
        LoadSlot(Boots,    build.BootsItemId);
        LoadSlot(Cape,     build.CapeItemId);
        LoadSlot(Bag,      build.BagItemId);
        LoadSlot(MainHand, build.MainHandItemId);
        LoadSlot(OffHand,  build.OffHandItemId);
        LoadSlot(Potion,   build.PotionItemId);
        LoadSlot(Food,     build.FoodItemId);
        LoadSlot(Mount,    build.MountItemId);

        LoadSlot(Extra1,   build.Extra1ItemId);
        LoadSlot(Extra2,   build.Extra2ItemId);
        LoadSlot(Extra3,   build.Extra3ItemId);
        LoadSlot(Extra4,   build.Extra4ItemId);

        Potion.Quantity = build.PotionQuantity;
        Food.Quantity   = build.FoodQuantity;

        RefreshOffHandLock();
        Browser.Initialize();
        IsDirty = false;
    }

    // ── Comandos ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;

        var build = _editingBuild ?? new Build();

        build.Name        = Name.Trim();
        build.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        build.HeadItemId     = Head.ItemId;
        build.ChestItemId    = Chest.ItemId;
        build.BootsItemId    = Boots.ItemId;
        build.CapeItemId     = Cape.ItemId;
        build.BagItemId      = Bag.ItemId;
        build.MainHandItemId = MainHand.ItemId;
        build.IsTwoHanded    = IsTwoHandedEquipped();
        build.OffHandItemId  = OffHand.IsLocked ? null : OffHand.ItemId;
        build.PotionItemId   = Potion.ItemId;
        build.PotionQuantity = Potion.Quantity;
        build.FoodItemId     = Food.ItemId;
        build.FoodQuantity   = Food.Quantity;
        build.MountItemId    = Mount.ItemId;

        build.Extra1ItemId   = Extra1.ItemId;
        build.Extra2ItemId   = Extra2.ItemId;
        build.Extra3ItemId   = Extra3.ItemId;
        build.Extra4ItemId   = Extra4.ItemId;

        if (_editingBuild is null)
            await _buildService.CreateBuildAsync(build);
        else
            await _buildService.UpdateBuildAsync(build);

        IsDirty = false;
        DialogService.Instance.MensajeQueue.Enqueue($"✅ Build \"{build.Name}\" guardada.");
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        IsDirty = false;
        Cancelled?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerable<SlotVm> AllSlots =>
        [Head, Chest, Boots, Cape, Bag, MainHand, OffHand, Potion, Food, Mount,
         Extra1, Extra2, Extra3, Extra4];

    private void LoadSlot(SlotVm slot, string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        slot.SetItem(_search.BuildItemVm(itemId, CancellationToken.None));
    }

    private bool IsTwoHandedEquipped()
    {
        if (MainHand.ItemId is null) return false;
        return _itemData.GetById(MainHand.ItemId)?.IsTwoHanded ?? false;
    }

    private void RefreshOffHandLock()
    {
        var is2H = IsTwoHandedEquipped();
        OffHand.IsLocked = is2H;
        if (is2H) OffHand.Reset();
    }
}
