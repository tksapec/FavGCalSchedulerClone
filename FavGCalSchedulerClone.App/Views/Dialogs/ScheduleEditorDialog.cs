using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class ScheduleEditorDialog
{
    private const double ScheduleMinWidthPhysical = 980;
    private const double ScheduleMinHeightPhysical = 620;
    private const double ScheduleDescriptionMinHeightPhysical = 120;

    public static ScheduleEditorResult? Show(
        DialogUiFactory ui,
        ScheduleEditorRequest request,
        Func<bool> hideOwner,
        Action showOwner)
    {
        var window = ui.CreateOwnedDialog(
            request.IsNew ? "スケジュールの追加" : "スケジュールの編集",
            1320,
            830,
            usePhysicalPixelSize: true,
            resizeMode: ResizeMode.CanResize,
            minWidth: ScheduleMinWidthPhysical,
            minHeight: ScheduleMinHeightPhysical,
            fitToWorkArea: true);
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
        var appReminderEditor = new ReminderListEditor(
            "アプリ内通知",
            ResolveReminderValues(request.AppReminderMinutesBeforeStart, request.IsAppReminderEnabled, request.ReminderMinutesBeforeStart),
            request.ReminderOptions);
        var googleEmailReminderEditor = new ReminderListEditor(
            "Googleメール通知",
            ResolveReminderValues(request.GoogleEmailReminderMinutesBeforeStart, request.IsGoogleEmailReminderEnabled, request.ReminderMinutesBeforeStart),
            request.ReminderOptions);
        var googleEmailReminder = new TextBlock
        {
            Text = request.GoogleEmailReminderDisplayText,
            Margin = new Thickness(0, ui.Y(4), 0, ui.Y(8))
        };
        var location = CreateEditableHistoryComboBox(request.Location, request.LocationHistory);
        var calendar = new ComboBox
        {
            ItemsSource = request.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = request.CalendarId
        };
        var color = ui.CreateColorComboBox(request.ColorId);
        var title = CreateEditableHistoryComboBox(request.Title, request.TitleHistory);
        var description = new TextBox
        {
            Text = request.Description,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = ui.Y(ScheduleDescriptionMinHeightPhysical),
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

        void ApplyDurationShortcut(TimeSpan duration)
        {
            if (TryCreateEndTimeFromDuration(startTime.Text, duration, out var endTimeText))
            {
                startTime.Text = NormalizeTimeText(startTime.Text);
                endTime.Text = endTimeText;
                isAllDay.IsChecked = false;
            }
        }

        var timeGroup = new GroupBox { Header = "開始時間／終了時間", Margin = new Thickness(0, 0, ui.X(10), ui.Y(10)), Padding = ui.Thickness(14, 14, 14, 6) };
        var timeGrid = new Grid();
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(230)) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(28)) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(230)) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
        var quickTimePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, ui.Y(2), 0, ui.Y(8)) };
        quickTimePanel.Children.Add(ShortcutButton("30分", () => ApplyDurationShortcut(TimeSpan.FromMinutes(30))));
        quickTimePanel.Children.Add(ShortcutButton("1時間", () => ApplyDurationShortcut(TimeSpan.FromHours(1))));
        quickTimePanel.Children.Add(ShortcutButton("2時間", () => ApplyDurationShortcut(TimeSpan.FromHours(2))));
        Grid.SetRow(quickTimePanel, 3);
        Grid.SetColumnSpan(quickTimePanel, 4);
        timeGrid.Children.Add(quickTimePanel);
        timeGroup.Content = timeGrid;

        var alarmGroup = new GroupBox { Header = "アラーム", Margin = new Thickness(0, 0, 0, ui.Y(10)), Padding = ui.Thickness(14, 14, 14, 6) };
        var alarmGrid = new Grid();
        alarmGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        alarmGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        alarmGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        alarmGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        alarmGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(appReminderEditor.Root, 0);
        Grid.SetColumn(appReminderEditor.Root, 0);
        alarmGrid.Children.Add(appReminderEditor.Root);
        Grid.SetRow(googleEmailReminderEditor.Root, 1);
        Grid.SetColumn(googleEmailReminderEditor.Root, 0);
        alarmGrid.Children.Add(googleEmailReminderEditor.Root);
        Grid.SetRow(googleEmailReminder, 2);
        Grid.SetColumn(googleEmailReminder, 0);
        alarmGrid.Children.Add(googleEmailReminder);
        alarmGroup.Content = alarmGrid;

        var upper = new Grid();
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(620)) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(timeGroup, 0);
        Grid.SetColumn(alarmGroup, 1);
        upper.Children.Add(timeGroup);
        upper.Children.Add(alarmGroup);

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
        ui.AddLabeledField(details, 0, 0, "件名", title, columnSpan: 5);
        ui.AddLabeledField(details, 1, 0, "場所", location);
        ui.AddLabeledField(details, 1, 2, "予定の色", color, rightMarginPhysicalPixels: 0);
        ui.AddLabeledField(details, 1, 4, "カレンダー", calendar);
        ui.AddLabeledField(details, 2, 0, "内容", description, columnSpan: 5, stretchVertically: true);
        detailsGroup.Content = details;

        var form = new Grid();
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(upper, 0);
        form.Children.Add(upper);
        Grid.SetRow(detailsGroup, 1);
        form.Children.Add(detailsGroup);

        var formScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = form
        };
        Grid.SetRow(formScrollViewer, 0);
        Grid.SetRowSpan(formScrollViewer, 2);
        root.Children.Add(formScrollViewer);

        var buttons = ui.DialogButtons(window, request.IsNew ? "登録" : "保存", "キャンセル");
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
        var appReminderValues = appReminderEditor.GetValues();
        var googleEmailReminderValues = googleEmailReminderEditor.GetValues();
        return new ScheduleEditorResult(
            calendar.SelectedValue?.ToString() ?? request.CalendarId,
            color.SelectedValue?.ToString(),
            startDate.SelectedDate ?? request.StartDate,
            endDate.SelectedDate ?? startDate.SelectedDate ?? request.StartDate,
            startTime.Text,
            endTime.Text,
            isAllDay.IsChecked == true,
            FirstOrDefaultOrNull(appReminderValues),
            appReminderValues.Count > 0,
            googleEmailReminderValues.Count > 0,
            location.Text,
            title.Text,
            description.Text,
            appReminderValues,
            googleEmailReminderValues);
    }

    private static IReadOnlyList<int> ResolveReminderValues(IReadOnlyList<int>? configured, bool enabled, int? fallback)
    {
        var values = CalendarEvent.NormalizeReminderMinutes(configured);
        if (values.Count > 0)
        {
            return values;
        }

        return enabled && fallback is int minutes ? [minutes] : [];
    }

    private static int? FirstOrDefaultOrNull(IReadOnlyList<int> values) => values.Count == 0 ? null : values[0];

    private static ComboBox CreateEditableHistoryComboBox(string text, IReadOnlyList<string> history)
    {
        var comboBox = new ComboBox
        {
            ItemsSource = history,
            Text = text
        };
        EditableHistoryComboBoxBehavior.Attach(comboBox);
        return comboBox;
    }

    private sealed class ReminderListEditor
    {
        private readonly IReadOnlyList<ReminderOption> _options;
        private readonly StackPanel _rows = new();
        private readonly List<ComboBox> _combos = [];
        private readonly CheckBox _enabled;

        public ReminderListEditor(string title, IReadOnlyList<int> values, IEnumerable<ReminderOption> options)
        {
            _options = options.Where(item => item.MinutesBeforeStart is not null).ToArray();
            _enabled = new CheckBox
            {
                Content = title,
                IsChecked = values.Count > 0,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Root = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            Root.Children.Add(_enabled);
            Root.Children.Add(_rows);
            var addButton = new Button
            {
                Content = "追加",
                Width = 64,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 0)
            };
            addButton.Click += (_, _) => AddRow(DefaultMinutes);
            Root.Children.Add(addButton);
            _enabled.Checked += (_, _) => SetRowsEnabled(true);
            _enabled.Unchecked += (_, _) => SetRowsEnabled(false);

            IEnumerable<int> initialValues = values.Count == 0 ? [DefaultMinutes] : values;
            foreach (var minutes in initialValues)
            {
                AddRow(minutes);
            }

            SetRowsEnabled(_enabled.IsChecked == true);
        }

        public StackPanel Root { get; }

        public IReadOnlyList<int> GetValues()
        {
            if (_enabled.IsChecked != true)
            {
                return [];
            }

            return CalendarEvent.NormalizeReminderMinutes(_combos.Select(combo => combo.SelectedValue).OfType<int>());
        }

        private int DefaultMinutes => _options.FirstOrDefault(item => item.MinutesBeforeStart == 10)?.MinutesBeforeStart ?? _options.FirstOrDefault()?.MinutesBeforeStart ?? 0;

        private void AddRow(int selectedMinutes)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var remove = new Button
            {
                Content = "-",
                Width = 28,
                MinWidth = 28,
                Margin = new Thickness(4, 0, 0, 0)
            };
            var combo = new ComboBox
            {
                ItemsSource = _options,
                DisplayMemberPath = nameof(ReminderOption.Label),
                SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart),
                SelectedValue = selectedMinutes,
                MinWidth = 150
            };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(combo);
            remove.Click += (_, _) =>
            {
                if (_combos.Count <= 1)
                {
                    combo.SelectedValue = DefaultMinutes;
                    return;
                }

                _combos.Remove(combo);
                _rows.Children.Remove(row);
            };
            _combos.Add(combo);
            _rows.Children.Add(row);
            SetRowsEnabled(_enabled.IsChecked == true);
        }

        private void SetRowsEnabled(bool enabled)
        {
            _rows.IsEnabled = enabled;
        }
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

    private static Button ShortcutButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 54,
            Height = 24,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0, 0, 4, 0)
        };
        button.Click += (_, _) => action();
        return button;
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

    internal static bool TryCreateEndTimeFromDuration(string? startTime, TimeSpan duration, out string endTime)
    {
        endTime = startTime ?? "";
        var normalized = NormalizeTimeText(startTime);
        if (!TimeSpan.TryParseExact(
                normalized,
                "hh\\:mm",
                CultureInfo.InvariantCulture,
                out var start)
            || start.Days != 0
            || start.Hours is < 0 or > 23
            || start.Minutes is < 0 or > 59
            || start.Seconds != 0)
        {
            return false;
        }

        var end = start.Add(duration);
        endTime = $"{end.Hours:00}:{end.Minutes:00}";
        return true;
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
    bool IsAppReminderEnabled,
    bool IsGoogleEmailReminderEnabled,
    string Location,
    string CalendarId,
    string? ColorId,
    string Title,
    string Description,
    IReadOnlyList<string> LocationHistory,
    IReadOnlyList<string> TitleHistory,
    IEnumerable<GoogleCalendarSelectionItem> AvailableCalendars,
    IEnumerable<ReminderOption> ReminderOptions,
    string GoogleEmailReminderDisplayText,
    IReadOnlyList<int>? AppReminderMinutesBeforeStart = null,
    IReadOnlyList<int>? GoogleEmailReminderMinutesBeforeStart = null);

internal sealed record ScheduleEditorResult(
    string CalendarId,
    string? ColorId,
    DateTime StartDate,
    DateTime EndDate,
    string StartTime,
    string EndTime,
    bool IsAllDay,
    int? ReminderMinutesBeforeStart,
    bool IsAppReminderEnabled,
    bool IsGoogleEmailReminderEnabled,
    string Location,
    string Title,
    string Description,
    IReadOnlyList<int>? AppReminderMinutesBeforeStart = null,
    IReadOnlyList<int>? GoogleEmailReminderMinutesBeforeStart = null);
