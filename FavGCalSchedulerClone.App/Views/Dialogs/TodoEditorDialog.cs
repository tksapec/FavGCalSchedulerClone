using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class TodoEditorDialog
{
    internal const double DueDateColumnPhysicalWidth = 230;
    internal const double UpperDueColumnWeight = 2.4;
    private const double TodoMinWidthPhysical = 560;
    private const double TodoMinHeightPhysical = 400;
    private const double TodoDescriptionMinHeightPhysical = 72;
    private static readonly string[] PriorityItems = ["A", "B", "C", "D", "E", "F"];

    public static TodoEditorResult? Show(DialogUiFactory ui, TodoEditorRequest request)
    {
        var window = ui.CreateOwnedDialog(
            request.IsNew ? "ＴＯＤＯの追加" : "ＴＯＤＯの編集",
            760,
            520,
            usePhysicalPixelSize: true,
            resizeMode: ResizeMode.CanResize,
            minWidth: TodoMinWidthPhysical,
            minHeight: TodoMinHeightPhysical,
            fitToWorkArea: true);
        var root = ui.CreateEditorDialogRoot();
        window.Content = root;

        var dueDate = ui.CreateDatePickerWithTodayButton(request.DueDate, out var dueDateEditor);
        var priority = new ComboBox { SelectedIndex = 0, ItemsSource = PriorityItems };
        priority.SelectedItem = string.IsNullOrWhiteSpace(request.Priority) ? "A" : request.Priority;
        FrameworkElement progressInput;
        FrameworkElement progressValue;
        Func<int> getProgress;
        var complete = new CheckBox
        {
            Content = "完了",
            IsChecked = request.Progress >= 100,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        if (request.IsNew)
        {
            complete.Margin = new Thickness(0);
            var done = new CheckBox { Content = "進捗(0%)", VerticalAlignment = VerticalAlignment.Center };
            done.Checked += (_, _) => done.Content = "進捗(100%)";
            done.Unchecked += (_, _) => done.Content = "進捗(0%)";
            done.IsChecked = request.Progress >= 100;
            complete.Checked += (_, _) => done.IsChecked = true;
            complete.Unchecked += (_, _) => done.IsChecked = false;
            done.Checked += (_, _) => complete.IsChecked = true;
            done.Unchecked += (_, _) => complete.IsChecked = false;
            progressInput = complete;
            progressValue = new TextBlock();
            getProgress = () => complete.IsChecked == true || done.IsChecked == true ? 100 : 0;
        }
        else
        {
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 10,
                LargeChange = 10,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = false,
                Width = ui.X(210),
                Value = request.Progress
            };
            var progressLabel = new TextBlock { Text = $"進捗 {request.Progress}%", VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, _) => progressLabel.Text = $"進捗 {(int)slider.Value}%";
            complete.Checked += (_, _) =>
            {
                slider.Value = 100;
                slider.IsEnabled = false;
            };
            complete.Unchecked += (_, _) =>
            {
                slider.IsEnabled = true;
                if ((int)slider.Value >= 100)
                {
                    slider.Value = Math.Min(90, Math.Max(0, request.Progress));
                }
            };
            if (complete.IsChecked == true)
            {
                slider.IsEnabled = false;
            }
            progressInput = slider;
            progressValue = progressLabel;
            getProgress = () => complete.IsChecked == true ? 100 : (int)slider.Value;
        }

        var calendarSelection = ResolveCalendarSelection(request);
        var calendar = new ComboBox
        {
            ItemsSource = calendarSelection.Options,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = calendarSelection.CalendarId
        };
        var color = ui.CreateColorComboBox(request.ColorId);
        var title = new TextBox { Text = request.Title };
        TextEditingBehavior.Attach(title);
        var description = new TextBox
        {
            Text = request.Description,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = ui.Y(TodoDescriptionMinHeightPhysical),
            VerticalContentAlignment = VerticalAlignment.Top
        };
        TextEditingBehavior.Attach(description);
        KeyboardNavigation.SetTabIndex(title, 0);
        KeyboardNavigation.SetTabIndex(dueDate, 1);
        KeyboardNavigation.SetTabIndex(priority, 2);
        KeyboardNavigation.SetTabIndex(progressInput, 3);
        KeyboardNavigation.SetTabIndex(color, 4);
        KeyboardNavigation.SetTabIndex(calendar, 5);
        KeyboardNavigation.SetTabIndex(description, 6);

        var validationMessage = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
            Margin = new Thickness(0, 4, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        void SaveIfValid()
        {
            if (!TryValidateInput(title.Text, dueDate.SelectedDate, out var error))
            {
                validationMessage.Text = error;
                FrameworkElement target = string.IsNullOrWhiteSpace(title.Text) ? title : dueDate;
                target.Focus();
                return;
            }

            window.DialogResult = true;
        }

        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                SaveIfValid();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                window.DialogResult = false;
                e.Handled = true;
            }
        };

        AddTodoEditorLayout(ui, root, window, request.IsNew, dueDateEditor, priority, progressInput, progressValue, complete, color, calendar, title, description, validationMessage, SaveIfValid);
        if (window.ShowDialog() != true)
        {
            return null;
        }

        return new TodoEditorResult(
            calendar.SelectedValue?.ToString() ?? calendarSelection.CalendarId,
            color.SelectedValue?.ToString(),
            dueDate.SelectedDate ?? request.DueDate,
            priority.SelectedItem?.ToString() ?? "A",
            getProgress(),
            title.Text,
            description.Text);
    }

    internal static (string CalendarId, IReadOnlyList<GoogleCalendarSelectionItem> Options) ResolveCalendarSelection(
        TodoEditorRequest request)
    {
        var options = request.AvailableCalendars.ToList();
        var requestedCalendarId = request.CalendarId;
        if (!string.IsNullOrWhiteSpace(requestedCalendarId)
            && options.Any(item => string.Equals(item.Id, requestedCalendarId, StringComparison.Ordinal)))
        {
            return (requestedCalendarId, options);
        }

        if (request.IsNew)
        {
            var fallback = options.FirstOrDefault(item => item.IsSelected) ?? options.FirstOrDefault();
            if (fallback is not null)
            {
                return (fallback.Id, options);
            }

            var fallbackId = string.IsNullOrWhiteSpace(requestedCalendarId)
                ? GoogleCalendarDefaults.PrimaryCalendarId
                : requestedCalendarId;
            options.Add(new GoogleCalendarSelectionItem
            {
                Id = fallbackId,
                Summary = FormatUnavailableCalendarSummary(fallbackId, isExisting: false)
            });
            return (fallbackId, options);
        }

        if (!string.IsNullOrWhiteSpace(requestedCalendarId))
        {
            options.Insert(0, new GoogleCalendarSelectionItem
            {
                Id = requestedCalendarId,
                Summary = FormatUnavailableCalendarSummary(requestedCalendarId, isExisting: true)
            });
            return (requestedCalendarId, options);
        }

        var existingFallback = options.FirstOrDefault(item => item.IsSelected) ?? options.FirstOrDefault();
        if (existingFallback is not null)
        {
            return (existingFallback.Id, options);
        }

        options.Add(new GoogleCalendarSelectionItem
        {
            Id = GoogleCalendarDefaults.PrimaryCalendarId,
            Summary = "メインカレンダー"
        });
        return (GoogleCalendarDefaults.PrimaryCalendarId, options);
    }

    private static string FormatUnavailableCalendarSummary(string calendarId, bool isExisting)
    {
        if (string.Equals(calendarId, GoogleCalendarDefaults.PrimaryCalendarId, StringComparison.Ordinal))
        {
            return "メインカレンダー";
        }

        return isExisting ? $"現在のカレンダー ({calendarId})" : calendarId;
    }

    private static void AddTodoEditorLayout(
        DialogUiFactory ui,
        Grid root,
        Window window,
        bool isNew,
        FrameworkElement dueDate,
        ComboBox priority,
        FrameworkElement progressInput,
        FrameworkElement progressValue,
        CheckBox complete,
        ComboBox color,
        ComboBox calendar,
        TextBox title,
        TextBox description,
        TextBlock validationMessage,
        Action saveIfValid)
    {
        var dueGroup = new GroupBox { Header = "期限／進捗", Padding = ui.Thickness(16, 16, 16, 8), Margin = new Thickness(0, 0, ui.X(10), ui.Y(10)) };
        var dueGrid = new Grid();
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(DueDateColumnPhysicalWidth)) });
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(16)) });
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(110)) });
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dueGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dueGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ui.AddLabeledField(dueGrid, 0, 0, "期限", dueDate);
        ui.AddLabeledField(dueGrid, 0, 2, "優先度", priority, rightMarginPhysicalPixels: 0);
        var progressPanel = new StackPanel { Orientation = Orientation.Horizontal };
        progressPanel.Children.Add(progressInput);
        if (!ReferenceEquals(progressInput, complete))
        {
            progressPanel.Children.Add(complete);
        }
        if (progressValue is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
        {
            progressPanel.Children.Add(progressValue);
        }
        progressPanel.Margin = new Thickness(0, 0, 0, ui.Y(12));
        Grid.SetRow(progressPanel, 1);
        Grid.SetColumnSpan(progressPanel, 4);
        dueGrid.Children.Add(progressPanel);
        dueGroup.Content = dueGrid;

        var detailsGroup = new GroupBox { Header = "ToDo詳細", Padding = ui.Thickness(18, 14, 18, 10), Margin = new Thickness(0, 0, 0, ui.Y(10)) };
        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(214)) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(16)) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ui.AddLabeledField(details, 0, 0, "件名", title, columnSpan: 3);
        ui.AddLabeledField(details, 1, 0, "予定の色", color, rightMarginPhysicalPixels: 0);
        ui.AddLabeledField(details, 1, 2, "カレンダー", calendar);
        ui.AddLabeledField(details, 2, 0, "内容", description, columnSpan: 3, stretchVertically: true);
        detailsGroup.Content = details;
        var form = new Grid();
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(dueGroup, 0);
        form.Children.Add(dueGroup);
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

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(validationMessage);
        var save = new Button { Content = isNew ? "登録" : "保存", MinWidth = 96 };
        var cancel = new Button { Content = "キャンセル", MinWidth = 96 };
        save.Click += (_, _) => saveIfValid();
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        window.Loaded += (_, _) =>
        {
            title.Focus();
            title.SelectAll();
        };
    }

    internal static bool TryValidateInput(string? title, DateTime? dueDate, out string error)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            error = "件名を入力してください。";
            return false;
        }

        if (dueDate is null)
        {
            error = "期限を入力してください。";
            return false;
        }

        error = "";
        return true;
    }
}

internal sealed record TodoEditorRequest(
    bool IsNew,
    DateTime DueDate,
    string Priority,
    int Progress,
    string CalendarId,
    string? ColorId,
    string Title,
    string Description,
    IEnumerable<GoogleCalendarSelectionItem> AvailableCalendars);

internal sealed record TodoEditorResult(
    string CalendarId,
    string? ColorId,
    DateTime DueDate,
    string Priority,
    int Progress,
    string Title,
    string Description);