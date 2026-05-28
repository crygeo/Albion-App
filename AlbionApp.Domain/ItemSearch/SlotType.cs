namespace AlbionApp.Domain.ItemSearch;

/// <summary>
/// Slot de equipamiento de un ítem de Albion Online.
/// Los valores coinciden con el atributo <c>slottype</c> del XML del juego.
/// </summary>
public enum SlotType
{
    /// <summary>Ítem no equipable o slot desconocido.</summary>
    None = 0,

    Head,
    Armor,    // Pechera
    Shoes,    // Botas
    Cape,
    Bag,
    MainHand,
    OffHand,
    Potion,
    Food,
    Mount,
}
