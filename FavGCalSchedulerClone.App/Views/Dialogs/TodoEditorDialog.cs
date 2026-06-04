using System.Windows;
using System.Windows.Controls;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class TodoEditorDialog
{
    internal const double DueDateColumnPhysicalWidth = 230;
    internal const double UpperDueColumnWeight = 2.4;
    private static readonly string[] PriorityItems = ["A", "B", "C", "D", "E", "F"];

    public static TodoEditorResult? Show(DialogUiFactory ui, TodoEditorRequest request)
    {
        var window = ui.CreateOwnedDialog(request.IsNew ? "ＴＯＤＯの追加" : "ＴＯＤＯの編集", 824, 610, usePhysicalPixelSize: true);
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

        var calendar = new ComboBox
        {
            ItemsSource = request.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = request.CalendarId
        };
        var color = ui.CreateColorComboBox(request.ColorId);
        var title = new TextBox { Text = request.Title };
        var description = new TextBox
        {
            Text = request.Description,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = ui.Y(145),
            VerticalContentAlignment = VerticalAlignment.Top
        };

        AddTodoEditorLayout(ui, root, window, dueDateEditor, priority, progressInput, progressValue, complete, color, calendar, title, description);
        if (window.ShowDialog() != true)
        {
            return null;
        }

        return new TodoEditorResult(
            calendar.SelectedValue?.ToString() ?? request.CalendarId,
            color.SelectedValue?.ToString(),
            dueDate.SelectedDate ?? request.DueDate,
            priority.SelectedItem?.ToString() ?? "A",
            getProgress(),
            title.Text,
            description.Text);
    }

    private static void AddTodoEditorLayout(
        DialogUiFactory ui,
        Grid root,
        Window window,
        FrameworkElement dueDate,
        ComboBox priority,
        FrameworkElement progressInput,
        FrameworkElement progressValue,
        CheckBox complete,
        ComboBox color,
        ComboBox calendar,
        TextBox title,
        TextBox description)
    {
        var dueGroup = new GroupBox { Header = "期限／進捗", Padding = ui.Thickness(16, 16, 16, 8), Margin = new Thickness(0, 0, ui.X(10), ui.Y(10)) };
        var dueGrid = new Grid();
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(DueDateColumnPhysicalWidth)) });
        dueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dueGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dueGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ui.AddLabeledField(dueGrid, 0, 0, "期限", dueDate);
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
        Grid.SetColumnSpan(progressPanel, 2);
        dueGrid.Children.Add(progressPanel);
        dueGroup.Content = dueGrid;

        var priorityGroup = new GroupBox { Header = "優先度", Padding = ui.Thickness(16, 16, 16, 16), Margin = new Thickness(0, 0, 0, ui.Y(10)) };
        var priorityGrid = new Grid();
        priorityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(110)) });
        ui.AddLabeledField(priorityGrid, 0, 0, "優先度", priority);
        priorityGroup.Content = priorityGrid;

        var upper = new Grid();
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(UpperDueColumnWeight, GridUnitType.Star) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dueGroup, 0);
        Grid.SetColumn(priorityGroup, 1);
        upper.Children.Add(dueGroup);
        upper.Children.Add(priorityGroup);
        Grid.SetRow(upper, 0);
        root.Children.Add(upper);

        var detailsGroup = new GroupBox { Header = "ToDo詳細", Padding = ui.Thickness(18, 14, 18, 10), Margin = new Thickness(0, 0, 0, ui.Y(10)) };
        var details = new Grid();
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(214)) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ui.X(16)) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ui.AddLabeledField(details, 0, 0, "予定の色", color, rightMarginPhysicalPixels: 0);
        ui.AddLabeledField(details, 0, 2, "カレンダー", calendar);
        ui.AddLabeledField(details, 1, 0, "件名", title, columnSpan: 3);
        ui.AddLabeledField(details, 2, 0, "内容", description, columnSpan: 3, stretchVertically: true);
        detailsGroup.Content = details;
        Grid.SetRow(detailsGroup, 1);
        root.Children.Add(detailsGroup);

        var buttons = ui.DialogButtons(window, "設定", "キャンセル");
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
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
