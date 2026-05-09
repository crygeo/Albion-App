namespace AlbionApp.Domain.Crafting;

/// <summary>
/// Tipo de estación de crafteo.
/// Determina qué conjunto de bonificaciones se aplica al calcular la tasa de retorno.
/// </summary>
public enum CraftingStationType
{
    /// <summary>Estación pública en una ciudad con su bonificación de especialización.</summary>
    CityWorkshop,

    /// <summary>Estación en un Hideout (HO); la bonificación depende del nivel del HO y la zona.</summary>
    Hideout,
}
