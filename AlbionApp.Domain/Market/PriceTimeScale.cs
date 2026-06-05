namespace AlbionApp.Domain.Market;

/// <summary>
/// Escala de tiempo para consultas de precios históricos.
/// <see cref="Hours"/> define el rango de fecha (ahora - Hours → ahora).
/// <see cref="ApiTimeScale"/> es el parámetro time-scale de la API (1 | 6 | 24).
/// </summary>
public sealed record PriceTimeScale(string Label, int Hours, int ApiTimeScale)
{
    public static readonly PriceTimeScale H6  = new("6 horas",  6,    1);
    public static readonly PriceTimeScale H12 = new("12 horas", 12,   1);
    public static readonly PriceTimeScale H24 = new("24 horas", 24,   6);
    public static readonly PriceTimeScale D3  = new("3 días",   72,   6);
    public static readonly PriceTimeScale D7  = new("7 días",   168,  24);
    public static readonly PriceTimeScale D15 = new("15 días",  360,  24);
    public static readonly PriceTimeScale D30 = new("30 días",  720,  24);

    public static IReadOnlyList<PriceTimeScale> All { get; } =
        [H6, H12, H24, D3, D7, D15, D30];

    public static PriceTimeScale Default => D7;

    public static PriceTimeScale FromLabel(string? label)
        => All.FirstOrDefault(t => t.Label == label) ?? Default;
}
