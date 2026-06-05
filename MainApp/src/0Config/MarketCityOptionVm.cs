using AlbionApp.Domain.Market;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App._0Config;

public sealed partial class MarketCityOptionVm : ObservableObject
{
    public MarketCity City { get; init; } = null!;

    public string Name       => City.Name;
    public string GroupLabel => City.Group switch
    {
        MarketCityGroup.Main      => "Principales",
        MarketCityGroup.Secondary => "Secundarias",
        MarketCityGroup.Hidden    => "Ciudad oculta",
        MarketCityGroup.Expensive => "Cara",
        _                         => string.Empty,
    };

    [ObservableProperty] private bool _isSelected;
}
