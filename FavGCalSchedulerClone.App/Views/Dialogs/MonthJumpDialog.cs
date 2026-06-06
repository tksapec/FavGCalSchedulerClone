using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class MonthJumpDialog
{
    public static DateTime? Show(DialogUiFactory ui, DateTime currentMonth)
    {
        var dialog = ui.CreateOwnedDialog("年月へ移動", 260, 170);
        var root = ui.CreateDialogRoot();

        var yearBox = new TextBox
        {
            Text = currentMonth.Year.ToString(CultureInfo.InvariantCulture),
            Width = 80,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var monthBox = new TextBox
        {
            Text = currentMonth.Month.ToString(CultureInfo.InvariantCulture),
            Width = 48,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };
        var errorText = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        var inputRow = new StackPanel { Orientation = Orientation.Horizontal };
        inputRow.Children.Add(new TextBlock { Text = "年", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        inputRow.Children.Add(yearBox);
        inputRow.Children.Add(new TextBlock { Text = "月", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        inputRow.Children.Add(monthBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", IsCancel = true, MinWidth = 88 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        DateTime? selected = null;
        ok.Click += (_, _) =>
        {
            if (TryCreateTargetMonth(yearBox.Text, monthBox.Text, out var target, out var error))
            {
                selected = target;
                dialog.DialogResult = true;
                return;
            }

            errorText.Text = error;
        };

        root.Children.Add(new TextBlock { Text = "移動先の年月を入力してください。" });
        root.Children.Add(inputRow);
        root.Children.Add(errorText);
        root.Children.Add(buttons);
        dialog.Content = root;
        yearBox.SelectAll();
        yearBox.Focus();
        return dialog.ShowDialog() == true ? selected : null;
    }

    internal static bool TryCreateTargetMonth(string? yearText, string? monthText, out DateTime target, out string error)
    {
        target = default;
        error = "";
        if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || year is < 1900 or > 2100)
        {
            error = "年は1900から2100の範囲で入力してください。";
            return false;
        }

        if (!int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || month is < 1 or > 12)
        {
            error = "月は1から12の範囲で入力してください。";
            return false;
        }

        target = new DateTime(year, month, 1);
        return true;
    }
}
