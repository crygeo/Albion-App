using System.Collections.ObjectModel;
using System.Windows;
using Albion_App.Components.Item;
using AlbionApp.Application.UseCases.SearchItems;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App._3Builds;

public partial class BuildItemBrowserVm : ObservableObject
{
    private readonly SearchItemsUseCase _search;
    private readonly ItemBaseVmFactory  _factory;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _filter = "";

    public ObservableCollection<ItemBaseVm> Results { get; } = [];

    public BuildItemBrowserVm(SearchItemsUseCase search, ItemBaseVmFactory factory)
    {
        _search  = search;
        _factory = factory;
    }

    // Carga inicial al abrir el editor.
    public void Initialize() => _ = RunSearchAsync("Casco", CancellationToken.None);

    partial void OnFilterChanged(string value)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = RunSearchAsync(value, _cts.Token);
    }

    private async Task RunSearchAsync(string filter, CancellationToken ct)
    {
        try
        {
            var query = new SearchItemsQuery(
                RawText:            filter,
                TierFilter:         null,
                EnchantmentFilter:  null,
                MinCategoryValue:   null,
                MaxCategoryValue:   null,
                MaxResults:         150);

            var result = await _search.ExecuteAsync(query, ct).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Results.Clear();
                foreach (var hit in result.Hits)
                    Results.Add(_factory.Create(hit, ct));
            });
        }
        catch (OperationCanceledException) { }
    }
}
