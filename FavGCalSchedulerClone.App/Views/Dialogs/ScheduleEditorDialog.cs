using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class ScheduleEditorDialog
{
    public static ScheduleEditorResult? Show(
        DialogUiFactory ui,
        ScheduleEditorRequest request,
        Func<bool> hideOwner,
        Action showOwner)
    {
        var window = ui.CreateOwnedDialog(request.IsNew ? "スケジュールの追加" : "スケジュールの編集", 1320, 830, usePhysicalPixelSize: true);
        var root = ui.CreateEditorDialogRoot();
        window.Content = root;

        var startDate = ui.CreateDatePickerWithTodayButton(request.StartDate, out var startDateEditor);
        var endDate = ui.CreateDatePickerWithTodayButton(request.EndDate, out var endDateEditor);
        var startTime = TimeComboBox(request.StartTime);
        var endTime = TimeComboBox(request.EndTime);
        var dayCount = new TextBox { Width = ui.X(64), Text = "1", HorizontalContentAlignment = HorizontalAlignment.Right };
        var isAllDay = new CheckBox
        {
            Content = "終日",
            IsChecked = request.IsAllDay,
            VerticalAlignment = VerticalAlignment.Center
        };
        var reminder = new ComboBox
        {
            ItemsSource = request.ReminderOptions,
            DisplayMemberPath = nameof(ReminderOption.Label),
            SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart),
            SelectedValue = request.ReminderMinutesBeforeStart
        };
        var location = new ComboBox
        {
            IsEditable = true,
            ItemsSource = request.LocationHistory,
            Text = request.Location
        };
        var calendar = new ComboBox
        {
            ItemsSource = request.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = request.CalendarId
        };
        var color = ui.CreateColorComboBox(request.ColorId);
        var title = new ComboBox
        {
            IsEditable = true,
            ItemsSource = request.TitleHistory,
            Text = request.Title
        };
        var description = new TextBox
        {
            Text = request.Description,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = ui.Y(255),
            VerticalContentAlignment = VerticalAlignment.Top
        };

        var updatingDateRange = false;
        void UpdateDayCount()
        {
            if (updatingDateRange || startDate.SelectedDate is null || endDate.SelectedDate is null)
            {
                return;
            }

            updatingDateRange = true;
            var days = Math.Max(1, (endDate.SelectedDate.Value.Date - startDate.SelectedDate.Value.Date).Days + 1);
            if (endDate.SelectedDate.Value.Date < startDate.SelectedDate.Value.Date)
            {
                endDate.SelectedDate = startDate.SelectedDate;
            }
            dayCount.Text = days.ToString();
            updatingDateRange = false;
        }

        void UpdateEndDateFromCount()
        {
            if (updatingDateRange || startDate.SelectedDate is null || !int.TryParse(dayCount.Text, out var days))
            {
                return;
            }

            updatingDateRange = true;
            days = Math.Max(1, days);
            dayCount.Text = days.ToString();
            endDate.SelectedDate = startDate.SelectedDate.Value.Date.AddDays(days - 1);
            updatingDateRange = false;
        }

        startDate.SelectedDateChanged += (_, _) => UpdateDayCount();
        endDate.SelectedDateChanged += (_, _) => UpdateDayCount();
        dayCount.LostFocus += (_, _) => UpdateEndDateFromCount();
        UpdateDayCount();

        var timeGroup = new GroupBox { Header = "開始時間／終了時間", Margin = new Thickness(0, 0, ui.X(10), ui.Y(10)), Padding = ui.Thickness(14, 14, 14, 6) };
        var timeGrid = new Grid();
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(230)) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(28)) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(230)) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ui.AddLabeledField(timeGrid, 0, 0, "開始日", startDateEditor);
        ui.AddLabeledField(timeGrid, 0, 2, "終了日", endDateEditor);
        var dayPanel = new StackPanel { Orientation = Orientation.Horizontal };
        dayPanel.Children.Add(dayCount);
        dayPanel.Children.Add(new TextBlock { Text = " 日数", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        dayPanel.Margin = new Thickness(0, 0, 0, ui.Y(12));
        Grid.SetRow(dayPanel, 1);
        Grid.SetColumn(dayPanel, 2);
        timeGrid.Children.Add(dayPanel);
        ui.AddLabeledField(timeGrid, 2, 0, "開始時間", startTime);
        var rangeMark = new TextBlock { Text = "～", VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, ui.Y(20)) };
        Grid.SetRow(rangeMark, 2);
        Grid.SetColumn(rangeMark, 1);
        timeGrid.Children.Add(rangeMark);
        ui.AddLabeledField(timeGrid, 2, 2, "終了時間", endTime);
        isAllDay.Margin = new Thickness(ui.X(8), 0, 0, ui.Y(20));
        isAllDay.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(isAllDay, 2);
        Grid.SetColumn(isAllDay, 3);
        timeGrid.Children.Add(isAllDay);
        timeGroup.Content = timeGrid;

        var alarmGroup = new GroupBox { Header = "アラーム", Margin = new Thickness(0, 0, 0, ui.Y(10)), Padding = ui.Thickness(14, 14, 14, 6) };
        var alarmGrid = new Grid();
        alarmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ui.AddLabeledField(alarmGrid, 0, 0, "通知時間", reminder);
        alarmGroup.Content = alarmGrid;

        var upper = new Grid();
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(620)) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(timeGroup, 0);
        Grid.SetColumn(alarmGroup, 1);
        upper.Children.Add(timeGroup);
        upper.Children.Add(alarmGroup);
        Grid.SetRow(upper, 0);
        root.Children.Add(upper);

        var detailsGroup = new GroupBox { Header = "予定詳細", Padding = ui.Thickness(18, 14, 18, 10), Margin = new Thickness(0, 0, 0, ui.Y(10)) };
        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(16)) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(214)) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(16)) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(260)) });
        ui.AddLabeledField(details, 0, 0, "場所", location);
        ui.AddLabeledField(details, 0, 2, "予定の色", color, rightMarginPhysicalPixels: 0);
        ui.AddLabeledField(details, 0, 4, "カレンダー", calendar);
        ui.AddLabeledField(details, 1, 0, "件名", title, columnSpan: 5);
        ui.AddLabeledField(details, 2, 0, "内容", description, columnSpan: 5, stretchVertically: true);
        detailsGroup.Content = details;
        Grid.SetRow(detailsGroup, 1);
        root.Children.Add(detailsGroup);

        var buttons = ui.DialogButtons(window, "設定", "キャンセル");
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        var accepted = false;
        var shouldRestoreOwner = hideOwner();
        try
        {
            accepted = window.ShowDialog() == true;
        }
        finally
        {
            if (shouldRestoreOwner)
            {
                showOwner();
            }
        }

        if (!accepted)
        {
            return null;
        }

        UpdateEndDateFromCount();
        return new ScheduleEditorResult(
            calendar.SelectedValue?.ToString() ?? request.CalendarId,
            color.SelectedValue?.ToString(),
            startDate.SelectedDate ?? request.StartDate,
            endDate.SelectedDate ?? startDate.SelectedDate ?? request.StartDate,
            startTime.Text,
            endTime.Text,
            isAllDay.IsChecked == true,
            reminder.SelectedValue as int?,
            location.Text,
            title.Text,
            description.Text);
    }

    private static ComboBox TimeComboBox(string selected)
    {
        var combo = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            Text = selected,
            ItemsSource = TimeChoices().ToArray()
        };
        combo.LostKeyboardFocus += (_, _) => combo.Text = NormalizeTimeText(combo.Text);
        return combo;
    }

    internal static string NormalizeTimeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? "";
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 4
            && trimmed.All(char.IsDigit)
            && int.TryParse(trimmed[..2], out var compactHour)
            && int.TryParse(trimmed[2..], out var compactMinute)
            && compactHour is >= 0 and <= 23
            && compactMinute is >= 0 and <= 59)
        {
            return $"{compactHour:00}:{compactMinute:00}";
        }

        if (TimeSpan.TryParseExact(
            trimmed,
            ["h\\:mm", "hh\\:mm"],
            CultureInfo.InvariantCulture,
            out var time)
            && time.Days == 0
            && time.Hours is >= 0 and <= 23
            && time.Minutes is >= 0 and <= 59
            && time.Seconds == 0)
        {
            return $"{time.Hours:00}:{time.Minutes:00}";
        }

        return value;
    }

    private static IEnumerable<string> TimeChoices()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            for (var minute = 0; minute < 60; minute += 30)
            {
                yield return $"{hour:00}:{minute:00}";
            }
        }
    }
}

internal sealed record ScheduleEditorRequest(
    bool IsNew,
    DateTime StartDate,
    DateTime EndDate,
    string StartTime,
    string EndTime,
    bool IsAllDay,
    int? ReminderMinutesBeforeStart,
    string Location,
    string CalendarId,
    string? ColorId,
    string Title,
    string Description,
    IReadOnlyList<string> LocationHistory,
    IReadOnlyList<string> TitleHistory,
    IEnumerable<GoogleCalendarSelectionItem> AvailableCalendars,
    IEnumerable<ReminderOption> ReminderOptions);

internal sealed record ScheduleEditorResult(
    string CalendarId,
    string? ColorId,
    DateTime StartDate,
    DateTime EndDate,
    string StartTime,
    string EndTime,
    bool IsAllDay,
    int? ReminderMinutesBeforeStart,
    string Location,
    string Title,
    string Description);
