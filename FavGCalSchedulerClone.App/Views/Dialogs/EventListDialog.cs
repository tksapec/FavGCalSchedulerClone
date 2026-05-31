using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class EventListDialog
{
    internal static IReadOnlyList<string> ResultColumnHeaders { get; } =
    [
        "開始日時",
        "終了日時",
        "アラーム",
        "カレンダー",
        "場所",
        "件名",
        "内容",
        "概要"
    ];

    public static void Show(DialogUiFactory ui, EventListDialogRequest request)
    {
        var currentFilter = request.Filter;
        var eventItems = new ObservableCollection<CalendarEvent>(request.Events);
        var window = ui.CreateOwnedDialog(request.Title, 1180, 680);
        window.MinWidth = 900;
        window.MinHeight = 520;
        window.ResizeMode = ResizeMode.CanResize;

        var panel = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
        window.Content = panel;

        var status = new TextBlock { Margin = new Thickness(0, 4, 0, 0) };
        var toolbar = CreateToolbar(request, eventItems, status, currentFilter, filter => currentFilter = filter);
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);

        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 28 };
        close.Click += (_, _) => window.Close();
        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
        buttons.Children.Add(status);
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonPanel.Children.Add(close);
        DockPanel.SetDock(buttonPanel, Dock.Right);
        buttons.Children.Add(buttonPanel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var grid = new DataGrid
        {
            ItemsSource = eventItems,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 22,
            FontSize = 12,
            GridLinesVisibility = DataGridGridLinesVisibility.All
        };
        grid.MouseDoubleClick += async (_, e) =>
        {
            var calendarEvent = DataGridDoubleClickHelper.GetEditableRowItem<CalendarEvent>(e.OriginalSource);
            if (calendarEvent is null)
            {
                return;
            }

            await request.EditEventAsync(calendarEvent);
            await ReloadAsync(request, eventItems, status, currentFilter);
        };

        AddColumns(grid);
        panel.Children.Add(grid);
        UpdateStatus(status, eventItems, currentFilter);

        window.ShowDialog();
    }

    private static FrameworkElement CreateToolbar(
        EventListDialogRequest request,
        ObservableCollection<CalendarEvent> eventItems,
        TextBlock status,
        EventListFilter initialFilter,
        Action<EventListFilter> updateCurrentFilter)
    {
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 6) };
        var firstRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var secondRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

        var calendar = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) };
        var startDate = new DatePicker { SelectedDate = GetDisplayedStart(initialFilter), Width = 150, Margin = new Thickness(0, 0, 8, 0) };
        var endDate = new DatePicker { SelectedDate = GetDisplayedEnd(initialFilter), Width = 150, Margin = new Thickness(0, 0, 8, 0) };
        var range = new ComboBox { ItemsSource = SearchRangeOptions, SelectedValuePath = "Value", DisplayMemberPath = "Label", SelectedValue = initialFilter.Range, Width = 105, Margin = new Thickness(0, 0, 8, 0) };
        var kind = new ComboBox { ItemsSource = SearchKindOptions, SelectedValuePath = "Value", DisplayMemberPath = "Label", SelectedValue = initialFilter.KindFilter, Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        var query = new TextBox { Text = initialFilter.Query, MinWidth = 240, Margin = new Thickness(0, 0, 8, 0) };
        var search = new Button { Content = "検索", MinWidth = 72 };

        var calendarOptions = request.CalendarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => new Option<string?>(id, id))
            .Prepend(new Option<string?>("すべてのカレンダー", null))
            .ToArray();
        calendar.ItemsSource = calendarOptions;
        calendar.DisplayMemberPath = "Label";
        calendar.SelectedValuePath = "Value";
        calendar.SelectedValue = initialFilter.CalendarId;

        range.SelectionChanged += (_, _) =>
        {
            if (range.SelectedValue is not EventSearchRange selectedRange || selectedRange == EventSearchRange.Custom)
            {
                return;
            }

            var reference = startDate.SelectedDate ?? initialFilter.ReferenceDate;
            var (start, end) = ResolveDisplayedRange(selectedRange, reference);
            startDate.SelectedDate = start;
            endDate.SelectedDate = end;
        };

        async void SearchClick(object? sender, RoutedEventArgs e)
        {
            var nextFilter = CreateFilter(query, kind, range, startDate, endDate, calendar, initialFilter.ReferenceDate);
            updateCurrentFilter(nextFilter);
            await ReloadAsync(request, eventItems, status, nextFilter);
        }

        search.Click += SearchClick;
        secondRow.Children.Add(Label("カレンダーで絞り込み"));
        secondRow.Children.Add(calendar);
        secondRow.Children.Add(Label("種別"));
        secondRow.Children.Add(kind);
        secondRow.Children.Add(Label("検索文字列"));
        secondRow.Children.Add(query);
        secondRow.Children.Add(search);

        firstRow.Children.Add(Label("検索期間"));
        firstRow.Children.Add(startDate);
        firstRow.Children.Add(Label("〜"));
        firstRow.Children.Add(endDate);
        firstRow.Children.Add(Label("範囲"));
        firstRow.Children.Add(range);

        root.Children.Add(firstRow);
        root.Children.Add(secondRow);
        return root;
    }

    private static EventListFilter CreateFilter(
        TextBox query,
        ComboBox kind,
        ComboBox range,
        DatePicker startDate,
        DatePicker endDate,
        ComboBox calendar,
        DateTime fallbackReferenceDate)
    {
        var selectedStart = startDate.SelectedDate ?? fallbackReferenceDate;
        var selectedEnd = endDate.SelectedDate ?? selectedStart;
        var selectedRange = range.SelectedValue is EventSearchRange rangeValue ? rangeValue : EventSearchRange.Custom;
        if (selectedRange != EventSearchRange.All)
        {
            selectedRange = EventSearchRange.Custom;
        }

        return new EventListFilter(
            query.Text,
            kind.SelectedValue is EventKindFilter selectedKind ? selectedKind : EventKindFilter.All,
            selectedRange,
            selectedStart,
            calendar.SelectedValue as string,
            selectedStart,
            selectedEnd);
    }

    private static void AddColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[0], Binding = new Binding(nameof(CalendarEvent.ListStartText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[1], Binding = new Binding(nameof(CalendarEvent.ListEndText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[2], Binding = new Binding(nameof(CalendarEvent.ReminderDisplayText)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[3], Binding = new Binding(nameof(CalendarEvent.CalendarId)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[4], Binding = new Binding(nameof(CalendarEvent.Location)), Width = 140 });
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[5], Binding = new Binding(nameof(CalendarEvent.Title)), Width = 220 });
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[6], Binding = new Binding(nameof(CalendarEvent.DescriptionPreview)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = ResultColumnHeaders[7], Binding = new Binding(nameof(CalendarEvent.SummaryDisplayText)), Width = 240 });
    }

    private static async Task ReloadAsync(
        EventListDialogRequest request,
        ObservableCollection<CalendarEvent> eventItems,
        TextBlock status,
        EventListFilter filter)
    {
        var refreshed = await request.ReloadEventsAsync(filter);
        eventItems.Clear();
        foreach (var refreshedEvent in refreshed)
        {
            eventItems.Add(refreshedEvent);
        }

        UpdateStatus(status, eventItems, filter);
    }

    private static void UpdateStatus(TextBlock status, IReadOnlyCollection<CalendarEvent> eventItems, EventListFilter filter)
    {
        status.Text = $"検索期間({FormatRange(filter)}) 結果[{eventItems.Count}件]";
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
    }

    private static DateTime GetDisplayedStart(EventListFilter filter)
    {
        return filter.Range == EventSearchRange.Custom && filter.StartDate is { } start
            ? start
            : ResolveDisplayedRange(filter.Range, filter.ReferenceDate).Start;
    }

    private static DateTime GetDisplayedEnd(EventListFilter filter)
    {
        return filter.Range == EventSearchRange.Custom && filter.EndDate is { } end
            ? end
            : ResolveDisplayedRange(filter.Range, filter.ReferenceDate).End;
    }

    private static string FormatRange(EventListFilter filter)
    {
        var start = GetDisplayedStart(filter);
        var end = GetDisplayedEnd(filter);
        return filter.Range == EventSearchRange.All
            ? "全件"
            : $"{start:yyyy/MM/dd}〜{end:yyyy/MM/dd}";
    }

    private static (DateTime Start, DateTime End) ResolveDisplayedRange(EventSearchRange range, DateTime reference)
    {
        var date = reference.Date;
        return range switch
        {
            EventSearchRange.Day => (date, date),
            EventSearchRange.Month => (new DateTime(date.Year, date.Month, 1), new DateTime(date.Year, date.Month, 1).AddMonths(1).AddDays(-1)),
            EventSearchRange.All => (new DateTime(1900, 1, 1), new DateTime(2099, 12, 31)),
            _ => (new DateTime(date.Year, 1, 1), new DateTime(date.Year, 12, 31))
        };
    }

    private static IReadOnlyList<Option<EventKindFilter>> SearchKindOptions { get; } =
    [
        new("すべて", EventKindFilter.All),
        new("予定", EventKindFilter.Schedule),
        new("ToDo", EventKindFilter.Todo)
    ];

    private static IReadOnlyList<Option<EventSearchRange>> SearchRangeOptions { get; } =
    [
        new("1日", EventSearchRange.Day),
        new("月間", EventSearchRange.Month),
        new("年間", EventSearchRange.Year),
        new("期間指定", EventSearchRange.Custom),
        new("全件", EventSearchRange.All)
    ];

    private sealed record Option<T>(string Label, T Value);
}

internal sealed record EventListDialogRequest(
    string Title,
    IReadOnlyList<CalendarEvent> Events,
    EventListFilter Filter,
    IReadOnlyList<string> CalendarIds,
    Func<EventListFilter, Task<IReadOnlyList<CalendarEvent>>> ReloadEventsAsync,
    Func<CalendarEvent, Task> EditEventAsync);
