using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private CalendarViewMode? _searchReturnViewMode;
    private int _searchGeneration;

    public async Task RunCurrentYearSearchAsync()
    {
        var searchWasVisible = IsSearchResultsVisible;
        var previousView = CurrentViewMode;
        var year = CurrentMonth.Year;
        var query = SearchQuery;
        var generation = Interlocked.Increment(ref _searchGeneration);

        if (!await RunSearchForYearAsync(year, query, generation, previousView))
        {
            return;
        }

        if (!searchWasVisible || previousView != CalendarViewMode.Month)
        {
            _searchReturnViewMode = previousView;
        }

        if (CurrentViewMode != CalendarViewMode.Month)
        {
            CurrentViewMode = CalendarViewMode.Month;
        }
    }

    private async Task<bool> RunSearchForYearAsync(
        int year,
        string query,
        int generation,
        CalendarViewMode expectedView)
    {
        var results = await SearchYearEventsAsync(new DateTime(year, 1, 1), query);
        if (generation != Volatile.Read(ref _searchGeneration)
            || !string.Equals(SearchQuery, query, StringComparison.Ordinal)
            || CurrentViewMode != expectedView)
        {
            return false;
        }

        _searchResultsYear = year;
        var selectedSearchResult = SelectedSearchResult;
        _searchResults.ReplaceAll(results);
        SelectedSearchResult = null;
        if (selectedSearchResult is not null && ReferenceEquals(SelectedEvent, selectedSearchResult))
        {
            SelectedEvent = null;
        }

        IsSearchResultsVisible = true;
        OnPropertyChanged(nameof(SearchResultsScopeText));
        Status = string.IsNullOrWhiteSpace(query)
            ? $"{year}年の予定を表示しています: {results.Count}件"
            : $"「{query.Trim()}」の検索結果: {results.Count}件";
        return true;
    }

    public async Task RefreshCurrentYearSearchAsync()
    {
        if (!IsSearchResultsVisible)
        {
            return;
        }

        var year = _searchResultsYear ?? CurrentMonth.Year;
        var query = SearchQuery;
        var expectedView = CurrentViewMode;
        var generation = Interlocked.Increment(ref _searchGeneration);
        await RunSearchForYearAsync(year, query, generation, expectedView);
    }

    private void ClearCurrentYearSearch()
    {
        Interlocked.Increment(ref _searchGeneration);
        var returnViewMode = _searchReturnViewMode;
        var selectedSearchResult = SelectedSearchResult;
        _searchReturnViewMode = null;
        SearchQuery = "";
        _searchResults.Clear();
        _searchResultsYear = null;
        SelectedSearchResult = null;
        if (selectedSearchResult is not null && ReferenceEquals(SelectedEvent, selectedSearchResult))
        {
            SelectedEvent = null;
        }

        IsSearchResultsVisible = false;
        OnPropertyChanged(nameof(SearchResultsScopeText));

        if (returnViewMode is { } viewMode && CurrentViewMode == CalendarViewMode.Month)
        {
            CurrentViewMode = viewMode;
        }

        Status = "検索結果を閉じました。";
    }
}
