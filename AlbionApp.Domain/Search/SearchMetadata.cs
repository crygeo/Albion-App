namespace AlbionApp.Domain.Search;

/// <summary>
/// Metadata estructural extraída de una clave de localización durante la construcción
/// del índice de búsqueda.
///
/// Se calcula UNA SOLA VEZ por entrada en tiempo de startup, eliminando el parsing
/// repetitivo en cada búsqueda del usuario.
///
/// Inmutable y por valor (<c>readonly record struct</c>) — sin heap allocations al
/// pasarla entre métodos y sin boxing al almacenarla en arrays.
/// </summary>
public readonly record struct SearchMetadata(
    int? Tier,
    int? Enchantment);
