using System.Windows.Controls;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class SearchDialog
{
    public static SearchDialogResult? Show(DialogUiFactory ui, SearchDialogRequest request)
    {
        var window = ui.CreateOwnedDialog("スケジュール検索", 560, 380);
        var root = ui.CreateDialogRoot();
        window.Content = root;

        var start = new DatePicker { SelectedDate = request.StartDate };
        var end = new DatePicker { SelectedDate = request.EndDate };
        var query = new TextBox { Text = request.Query };
        var kind = new ComboBox { ItemsSource = SearchKindOptions, SelectedValuePath = "Value", DisplayMemberPath = "Label", SelectedValue = request.KindFilter };
        var range = new ComboBox { ItemsSource = SearchRangeOptions, SelectedValuePath = "Value", DisplayMemberPath = "Label", SelectedValue = request.Range };

        range.SelectionChanged += (_, _) =>
        {
            if (range.SelectedValue is not EventSearchRange selectedRange || selectedRange == EventSearchRange.Custom)
            {
                return;
            }

            var reference = start.SelectedDate ?? request.StartDate;
            var (rangeStart, rangeEnd) = ResolveDisplayedRange(selectedRange, reference);
            start.SelectedDate = rangeStart;
            end.SelectedDate = rangeEnd;
        };

        root.Children.Add(ui.SectionHeader("検索範囲"));
        root.Children.Add(ui.FormGrid(("開始", start, "終了", end)));
        root.Children.Add(ui.FormGrid(("範囲", range, "種別", kind)));
        root.Children.Add(ui.SectionHeader("条件"));
        root.Children.Add(ui.WideField("検索文字列", query));
        root.Children.Add(ui.DialogButtons(window, "検索", "キャンセル"));

        return window.ShowDialog() == true
            ? new SearchDialogResult(
                query.Text,
                kind.SelectedValue is EventKindFilter selectedKind ? selectedKind : EventKindFilter.All,
                range.SelectedValue is EventSearchRange selectedRange ? selectedRange : EventSearchRange.Custom,
                start.SelectedDate ?? request.StartDate,
                end.SelectedDate ?? request.EndDate)
            : null;
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

internal sealed record SearchDialogRequest(
    DateTime StartDate,
    DateTime EndDate,
    string Query = "",
    EventKindFilter KindFilter = EventKindFilter.All,
    EventSearchRange Range = EventSearchRange.Custom);

internal sealed record SearchDialogResult(
    string Query,
    EventKindFilter KindFilter = EventKindFilter.All,
    EventSearchRange Range = EventSearchRange.Custom,
    DateTime? StartDate = null,
    DateTime? EndDate = null);
