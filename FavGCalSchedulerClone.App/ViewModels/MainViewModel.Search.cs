using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private CalendarViewMode? _searchReturnViewMode;

    public async Task RunCurrentYearSearchAsync()
    {
        var searchWasVisible = IsSearchResultsVisible;
        var previousView = CurrentViewMode;
        _searchResultsYear = CurrentMonth.Year;
        await RunSearchForYearAsync(_searchResultsYear.Value);

        if (!searchWasVisible || previousView != CalendarViewMode.Month)
        {
            _searchReturnViewMode = previousView;
        }

        if (CurrentViewMode != CalendarViewMode.Month)
        {
            CurrentViewMode = CalendarViewMode.Month;
        }
    }

    private async Task RunSearchForYearAsync(int year)
    {
        var results = await SearchYearEventsAsync(new DateTime(year, 1, 1), SearchQuery);
        var selectedSearchResult = SelectedSearchResult;
        _searchResults.ReplaceAll(results);
        SelectedSearchResult = null;
        if (selectedSearchResult is not null && ReferenceEquals(SelectedEvent, selectedSearchResult))
        {
            SelectedEvent = null;
        }
        IsSearchResultsVisible = true;
        OnPropertyChanged(nameof(SearchResultsScopeText));
        Status = string.IsNullOrWhiteSpace(SearchQuery)
            ? $"{year}年の予定を表示しています: {results.Count}件"
            : $"「{SearchQuery.Trim()}」の検索結果: {results.Count}件";
    }

    public async Task RefreshCurrentYearSearchAsync()
    {
        if (IsSearchResultsVisible)
        {
            await RunSearchForYearAsync(_searchResultsYear ?? CurrentMonth.Year);
        }
    }

    private void ClearCurrentYearSearch()
    {
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
