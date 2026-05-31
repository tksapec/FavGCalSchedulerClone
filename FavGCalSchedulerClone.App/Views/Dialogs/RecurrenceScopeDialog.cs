using System.Windows;
using System.Windows.Controls;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class RecurrenceScopeDialog
{
    public static RecurrenceEditScope? Show(DialogUiFactory ui, RecurrenceScopeDialogRequest request)
    {
        var window = ui.CreateOwnedDialog(request.IsDelete ? "削除対象" : "編集対象", 420, 220);
        var root = ui.CreateDialogRoot();
        window.Content = root;

        root.Children.Add(ui.SectionHeader(request.IsDelete
            ? "どこまで削除するか選択してください。"
            : "どこまで反映するか選択してください。"));

        RecurrenceEditScope? selected = null;
        root.Children.Add(CreateScopeButton(window, "この予定のみ", RecurrenceEditScope.ThisOccurrence, value => selected = value));
        root.Children.Add(CreateScopeButton(window, "この予定以降", RecurrenceEditScope.ThisAndFollowing, value => selected = value));
        root.Children.Add(CreateScopeButton(window, "すべての予定", RecurrenceEditScope.AllEvents, value => selected = value));

        var cancel = new Button { Content = "キャンセル", MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        cancel.Click += (_, _) => window.DialogResult = false;
        root.Children.Add(cancel);

        return window.ShowDialog() == true ? selected : null;
    }

    private static Button CreateScopeButton(Window window, string text, RecurrenceEditScope scope, Action<RecurrenceEditScope> setSelected)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            Height = 34
        };
        button.Click += (_, _) =>
        {
            setSelected(scope);
            window.DialogResult = true;
        };
        return button;
    }
}

internal sealed record RecurrenceScopeDialogRequest(bool IsDelete);
