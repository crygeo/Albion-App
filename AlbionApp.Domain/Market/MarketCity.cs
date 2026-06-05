namespace AlbionApp.Domain.Market;

public enum MarketCityGroup
{
    /// <summary>5 ciudades reales principales — seleccionadas por defecto.</summary>
    Main,
    /// <summary>Áreas de descanso — mercado secundario.</summary>
    Secondary,
    /// <summary>Ciudad oculta — requiere acceso especial.</summary>
    Hidden,
    /// <summary>Ciudad más cara del juego.</summary>
    Expensive,
}

/// <summary>
/// Ciudad del mercado de Albion Online con su nombre para mostrar
/// y el nombre exacto que acepta la API de Albion Online Data Project.
/// </summary>
public sealed record MarketCity(string Name, string ApiLocation, MarketCityGroup Group)
{
    // ── Principales (default) ─────────────────────────────────────────────────
    public static readonly MarketCity Bridgewatch  = new("Bridgewatch",    "Bridgewatch",   MarketCityGroup.Main);
    public static readonly MarketCity FortSterling = new("Fort Sterling",  "Fort Sterling", MarketCityGroup.Main);
    public static readonly MarketCity Lymhurst     = new("Lymhurst",       "Lymhurst",      MarketCityGroup.Main);
    public static readonly MarketCity Martlock      = new("Martlock",       "Martlock",       MarketCityGroup.Main);
    public static readonly MarketCity Thetford      = new("Thetford",       "Thetford",       MarketCityGroup.Main);

    // ── Secundarias ───────────────────────────────────────────────────────────
    public static readonly MarketCity ArthursRest  = new("Arthur's Rest",  "ArthursRest",   MarketCityGroup.Secondary);
    public static readonly MarketCity MerlinsRest  = new("Merlin's Rest",  "MerlinsRest",   MarketCityGroup.Secondary);
    public static readonly MarketCity MorganaRest  = new("Morgana's Rest", "MorganaRest",   MarketCityGroup.Secondary);

    // ── Oculta ────────────────────────────────────────────────────────────────
    public static readonly MarketCity Caerleon     = new("Caerleon",        "Caerleon",       MarketCityGroup.Hidden);

    // ── Cara ──────────────────────────────────────────────────────────────────
    public static readonly MarketCity Brecilien    = new("Brecilien",       "Brecilien",      MarketCityGroup.Expensive);

    public static IReadOnlyList<MarketCity> All { get; } =
    [
        Bridgewatch, FortSterling, Lymhurst, Martlock, Thetford,
        ArthursRest, MerlinsRest, MorganaRest,
        Caerleon,
        Brecilien,
    ];

    /// <summary>Selección por defecto: las 5 ciudades principales.</summary>
    public static IReadOnlySet<string> DefaultApiLocations { get; } =
        new HashSet<string> { "Bridgewatch", "Fort Sterling", "Lymhurst", "Martlock", "Thetford" };

    public static MarketCity? FromApiLocation(string apiLocation)
        => All.FirstOrDefault(c => c.ApiLocation == apiLocation);
}
