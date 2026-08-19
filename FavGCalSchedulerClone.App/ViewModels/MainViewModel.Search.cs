using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{
    private CalendarViewMode? _searchReturnViewMode;

    public async Task RunCurrentYearSearchAsync()
    {
        if (!IsSearchResultsVisible)
        {
            _searchReturnViewMode = CurrentViewMode;
        }

        if (CurrentViewMode != CalendarViewMode.Month)
        {
            CurrentViewMode = CalendarViewMode.Month;
        }

        _searchResultsYear = CurrentMonth.Year;
        await RunSearchForYearAsync(_searchResultsYear.Value);
    }

    private async Task RunSearchForYearAsync(int year)
    {
        var results = await SearchYearEventsAsync(new DateTime(year, 1, 1), SearchQuery);
        _searchResults.ReplaceAll(results);
        SelectedSearchResult = null;
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
        _searchReturnViewMode = null;
        SearchQuery = "";
        _searchResults.Clear();
        _searchResultsYear = null;
        SelectedSearchResult = null;
        IsSearchResultsVisible = false;
        OnPropertyChanged(nameof(SearchResultsScopeText));

        if (returnViewMode is { } viewMode && CurrentViewMode == CalendarViewMode.Month)
        {
            CurrentViewMode = viewMode;
        }

        Status = "検索結果を閉じました。";
    }
}
