namespace AlbionApp.Domain.Market;

/// <summary>
/// Servidor de Albion Online disponible para consultar precios de mercado.
/// La BaseUrl apunta a la API de Albion Online Data Project.
/// </summary>
public sealed record AlbionServer(string Name, string BaseUrl)
{
    public static readonly AlbionServer America = new("América", "https://west.albion-online-data.com");
    public static readonly AlbionServer Asia    = new("Asia",    "https://east.albion-online-data.com");
    public static readonly AlbionServer Europe  = new("Europa",  "https://europe.albion-online-data.com");

    public static IReadOnlyList<AlbionServer> All { get; } = [America, Asia, Europe];

    public static AlbionServer FromName(string? name) =>
        All.FirstOrDefault(s => s.Name == name) ?? America;
}
