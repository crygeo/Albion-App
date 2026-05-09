namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Decisión sobre si craftear o comprar un ingrediente específico.
/// El usuario puede sobrescribir la decisión por ítem.
/// </summary>
public enum CraftDecision
{
    /// <summary>Craftear si el ítem tiene receta; comprar si no.</summary>
    Auto,

    /// <summary>Siempre craftear, incluso si resulta más caro.</summary>
    Craft,

    /// <summary>Siempre comprar en el mercado.</summary>
    Buy,
}
