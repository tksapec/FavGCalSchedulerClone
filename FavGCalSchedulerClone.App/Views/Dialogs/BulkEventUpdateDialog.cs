using System.Windows;
using System.Windows.Controls;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class BulkEventUpdateDialog
{
    public static BulkEventUpdateRequest? Show(DialogUiFactory ui, IReadOnlyList<string> calendarIds)
    {
        var window = ui.CreateOwnedDialog("一括編集", 420, 320, resizeMode: ResizeMode.NoResize);
        var root = new StackPanel { Margin = new Thickness(12) };
        window.Content = root;

        var calendarEnabled = new CheckBox { Content = "カレンダーを変更", Margin = new Thickness(0, 0, 0, 4) };
        var calendar = new ComboBox { ItemsSource = calendarIds, MinWidth = 220, IsEnabled = false, Margin = new Thickness(20, 0, 0, 8) };
        calendar.SelectedIndex = calendarIds.Count > 0 ? 0 : -1;
        calendarEnabled.Checked += (_, _) => calendar.IsEnabled = true;
        calendarEnabled.Unchecked += (_, _) => calendar.IsEnabled = false;

        var colorEnabled = new CheckBox { Content = "色を変更", Margin = new Thickness(0, 0, 0, 4) };
        var color = ui.CreateColorComboBox(null);
        color.Margin = new Thickness(20, 0, 0, 8);
        color.IsEnabled = false;
        colorEnabled.Checked += (_, _) => color.IsEnabled = true;
        colorEnabled.Unchecked += (_, _) => color.IsEnabled = false;

        var reminderEnabled = new CheckBox { Content = "通知設定を変更", Margin = new Thickness(0, 0, 0, 4) };
        var reminderPanel = new StackPanel { Margin = new Thickness(20, 0, 0, 8), IsEnabled = false };
        var minutes = new ComboBox { ItemsSource = ReminderOption.Defaults, DisplayMemberPath = "Label", SelectedValuePath = "Minutes", SelectedValue = 10, Width = 160 };
        var appReminder = new CheckBox { Content = "アプリ内通知", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
        var emailReminder = new CheckBox { Content = "Googleメール通知", IsChecked = false, Margin = new Thickness(0, 4, 0, 0) };
        reminderPanel.Children.Add(minutes);
        reminderPanel.Children.Add(appReminder);
        reminderPanel.Children.Add(emailReminder);
        reminderEnabled.Checked += (_, _) => reminderPanel.IsEnabled = true;
        reminderEnabled.Unchecked += (_, _) => reminderPanel.IsEnabled = false;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "適用", MinWidth = 96, IsDefault = true };
        var cancel = new Button { Content = "キャンセル", MinWidth = 96, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        root.Children.Add(calendarEnabled);
        root.Children.Add(calendar);
        root.Children.Add(colorEnabled);
        root.Children.Add(color);
        root.Children.Add(reminderEnabled);
        root.Children.Add(reminderPanel);
        root.Children.Add(buttons);

        BulkEventUpdateRequest? result = null;
        ok.Click += (_, _) =>
        {
            result = new BulkEventUpdateRequest(
                CalendarId: calendarEnabled.IsChecked == true ? calendar.SelectedItem as string : null,
                ColorId: colorEnabled.IsChecked == true ? color.SelectedValue as string : null,
                ReminderMinutesBeforeStart: reminderEnabled.IsChecked == true ? minutes.SelectedValue as int? : null,
                AppReminderEnabled: reminderEnabled.IsChecked == true ? appReminder.IsChecked == true : null,
                GoogleEmailReminderEnabled: reminderEnabled.IsChecked == true ? emailReminder.IsChecked == true : null,
                UpdateColor: colorEnabled.IsChecked == true);
            window.DialogResult = true;
            window.Close();
        };

        return window.ShowDialog() == true ? result : null;
    }
}
