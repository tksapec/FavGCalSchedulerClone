using System.Windows;
using System.Windows.Controls;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class SearchDialog
{
    public static SearchDialogResult? Show(DialogUiFactory ui, SearchDialogRequest request)
    {
        var window = ui.CreateOwnedDialog("スケジュール検索", 520, 320);
        var root = ui.CreateDialogRoot();
        window.Content = root;

        var start = new DatePicker { SelectedDate = request.StartDate, IsEnabled = false };
        var end = new DatePicker { SelectedDate = request.EndDate, IsEnabled = false };
        var query = new TextBox { Text = request.Query };

        root.Children.Add(ui.SectionHeader("検索範囲"));
        root.Children.Add(ui.FormGrid(("開始", start, "終了", end)));
        root.Children.Add(ui.SectionHeader("条件"));
        root.Children.Add(ui.WideField("検索文字列", query));
        root.Children.Add(ui.DialogButtons(window, "検索", "キャンセル"));

        return window.ShowDialog() == true
            ? new SearchDialogResult(query.Text)
            : null;
    }
}

internal sealed record SearchDialogRequest(DateTime StartDate, DateTime EndDate, string Query = "");

internal sealed record SearchDialogResult(string Query);
