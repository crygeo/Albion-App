using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibEvents.Entities;
using LibEvents.Services;

namespace Albion_App._5Events;

public enum HistogramGranularity
{
    Day,
    Week,
    Month
}

public partial class EventStatsVm : ObservableObject
{
    private readonly BuildService _buildService;
    private List<GuildEvent> _allEvents = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Buckets))]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private HistogramGranularity _granularity = HistogramGranularity.Day;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Buckets))]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private bool _onlyCompleted;

    public EventStatsVm(BuildService buildService) => _buildService = buildService;

    public async Task LoadAsync()
    {
        _allEvents = await _buildService.GetEventsAsync();
        OnPropertyChanged(nameof(Buckets));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(MostUsedComposition));
        OnPropertyChanged(nameof(HasMostUsedComposition));
    }

    [RelayCommand]
    private void SelectGranularity(HistogramGranularity granularity) => Granularity = granularity;

    public IEnumerable<HistogramBucketVm> Buckets
    {
        get
        {
            var filtered = _allEvents
                .Where(e => e.Status == EventStatus.Closed || e.Status == EventStatus.Cancelled)
                .Where(e => e.ScheduledAt.HasValue)
                .Where(e => !OnlyCompleted || e.Status == EventStatus.Closed)
                .ToList();

            if (filtered.Count == 0)
                return [];

            var periods = BuildPeriodRange(filtered, Granularity);
            var counts = filtered
                .GroupBy(e => PeriodKey(e.ScheduledAt!.Value, Granularity))
                .ToDictionary(g => g.Key, g => g.Count());

            var maxCount = periods.Select(p => counts.GetValueOrDefault(p.Key, 0)).DefaultIfEmpty(0).Max();

            return periods
                .Select(p => new HistogramBucketVm(p.Label, counts.GetValueOrDefault(p.Key, 0), maxCount))
                .ToList();
        }
    }

    public bool HasData => Buckets.Any(b => b.Count > 0);

    public CompositionUsageVm? MostUsedComposition => null;

    public bool HasMostUsedComposition => false;

    // ── Agrupación por período ────────────────────────────────────────────────

    private static DateTime PeriodKey(DateTime date, HistogramGranularity granularity) => granularity switch
    {
        HistogramGranularity.Day   => date.Date,
        HistogramGranularity.Week  => StartOfWeek(date.Date),
        HistogramGranularity.Month => new DateTime(date.Year, date.Month, 1),
        _                          => date.Date
    };

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff);
    }

    private static DateTime NextPeriod(DateTime periodStart, HistogramGranularity granularity) => granularity switch
    {
        HistogramGranularity.Day   => periodStart.AddDays(1),
        HistogramGranularity.Week  => periodStart.AddDays(7),
        HistogramGranularity.Month => periodStart.AddMonths(1),
        _                          => periodStart.AddDays(1)
    };

    private static string PeriodLabel(DateTime periodStart, HistogramGranularity granularity) => granularity switch
    {
        HistogramGranularity.Day   => periodStart.ToString("dd MMM"),
        HistogramGranularity.Week  => WeekLabel(periodStart),
        HistogramGranularity.Month => periodStart.ToString("MMM yyyy"),
        _                          => periodStart.ToString("dd MMM")
    };

    private static string WeekLabel(DateTime weekStart)
    {
        var weekEnd = weekStart.AddDays(6);
        return weekStart.Month == weekEnd.Month
            ? $"Sem {weekStart:dd}–{weekEnd:dd MMM}"
            : $"Sem {weekStart:dd MMM}–{weekEnd:dd MMM}";
    }

    private static List<(DateTime Key, string Label)> BuildPeriodRange(List<GuildEvent> events, HistogramGranularity granularity)
    {
        var dates = events.Select(e => e.ScheduledAt!.Value).ToList();
        var first = PeriodKey(dates.Min(), granularity);
        var last  = PeriodKey(dates.Max(), granularity);

        var periods = new List<(DateTime, string)>();
        for (var current = first; current <= last; current = NextPeriod(current, granularity))
            periods.Add((current, PeriodLabel(current, granularity)));

        return periods;
    }
}
