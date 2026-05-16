using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var repository = new CalendarRepository();
        _viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DayList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await ShowScheduleDialogAsync();
    }

    private async void AddScheduleMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowScheduleDialogAsync();
    }

    private async void AddTodoMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowTodoDialogAsync();
    }

    private async void ScheduleListMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowEventListDialogAsync("スケジュール一覧", "");
    }

    private async void SearchMenu_Click(object sender, RoutedEventArgs e)
    {
        await ShowSearchDialogAsync();
    }

    private void WeatherAreaMenu_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "天気予報表示エリアの変更は後続フェーズで実装します。", "天気予報表示エリアの変更", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SettingsMenu_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsDialog();
    }

    private void AboutMenu_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "FavGCalSchedulerClone\nVersion 0.1.0\n\nFavGCalScheduler互換を目指した個人利用向けWindowsカレンダーです。",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task ShowScheduleDialogAsync()
    {
        var date = _viewModel.SelectedDay?.Date ?? DateTime.Today;
        var window = CreateOwnedDialog("スケジュールの追加", 560, 430);
        var root = CreateDialogRoot();
        window.Content = root;

        var startDate = new DatePicker { SelectedDate = date };
        var endDate = new DatePicker { SelectedDate = date };
        var days = new TextBox { Text = "1", IsReadOnly = true };
        var startTime = new ComboBox { IsEditable = true, Text = "09:00" };
        var endTime = new ComboBox { IsEditable = true, Text = "10:00" };
        foreach (var time in TimeChoices())
        {
            startTime.Items.Add(time);
            endTime.Items.Add(time);
        }

        var isAllDay = new CheckBox { Content = "終日", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var notify = new ComboBox { IsEnabled = false, SelectedIndex = 0, ItemsSource = new[] { "通知なし", "10分前", "30分前", "1時間前" } };
        var location = new TextBox();
        var color = new ComboBox { IsEnabled = false, SelectedIndex = 0, ItemsSource = new[] { "既定" } };
        var calendar = new ComboBox { SelectedIndex = 0, ItemsSource = _viewModel.CalendarNames };
        var title = new TextBox();
        var description = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 92, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        root.Children.Add(Labeled("開始日", startDate, 0, 0));
        root.Children.Add(Labeled("終了日", endDate, 0, 2));
        root.Children.Add(Labeled("日数", days, 1, 0));
        root.Children.Add(Labeled("開始時間", startTime, 1, 2));
        root.Children.Add(Labeled("終了時間", endTime, 2, 2));
        root.Children.Add(Place(isAllDay, 2, 0, 1, 2));
        root.Children.Add(Labeled("通知時間", notify, 3, 0));
        root.Children.Add(Labeled("場所", location, 4, 0, 1, 4));
        root.Children.Add(Labeled("予定の色", color, 5, 0));
        root.Children.Add(Labeled("カレンダー", calendar, 5, 2));
        root.Children.Add(Labeled("件名", title, 6, 0, 1, 4));
        root.Children.Add(Labeled("内容", description, 7, 0, 1, 4));
        root.Children.Add(Place(DialogButtons(window, "　設定　", "　キャンセル　"), 8, 0, 1, 4));

        if (window.ShowDialog() == true)
        {
            _viewModel.BeginNewEvent(startDate.SelectedDate ?? date);
            _viewModel.StartDate = startDate.SelectedDate ?? date;
            _viewModel.EndDate = endDate.SelectedDate ?? _viewModel.StartDate;
            _viewModel.StartTime = startTime.Text;
            _viewModel.EndTime = endTime.Text;
            _viewModel.IsAllDay = isAllDay.IsChecked == true;
            _viewModel.Location = location.Text;
            _viewModel.Title = title.Text;
            _viewModel.Description = description.Text;
            await _viewModel.SaveCurrentEventAsync();
        }
    }

    private async Task ShowTodoDialogAsync()
    {
        var date = _viewModel.SelectedDay?.Date ?? DateTime.Today;
        var window = CreateOwnedDialog("ＴＯＤＯの追加", 520, 350);
        var root = CreateDialogRoot();
        window.Content = root;

        var dueDate = new DatePicker { SelectedDate = date };
        var priority = new ComboBox { SelectedIndex = 0, ItemsSource = new[] { "A", "B", "C", "D", "E", "F" } };
        var progress = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 210 };
        var color = new ComboBox { IsEnabled = false, SelectedIndex = 0, ItemsSource = new[] { "既定" } };
        var calendar = new ComboBox { SelectedIndex = 0, ItemsSource = _viewModel.CalendarNames };
        var title = new TextBox();
        var description = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        root.Children.Add(Labeled("期限", dueDate, 0, 0));
        root.Children.Add(Labeled("優先度", priority, 1, 0));
        root.Children.Add(Labeled("進捗(0%)", progress, 2, 0, 1, 4));
        root.Children.Add(Labeled("予定の色", color, 3, 0));
        root.Children.Add(Labeled("カレンダー", calendar, 3, 2));
        root.Children.Add(Labeled("件名", title, 4, 0, 1, 4));
        root.Children.Add(Labeled("内容", description, 5, 0, 1, 4));
        root.Children.Add(Place(DialogButtons(window, "　設定　", "　キャンセル　"), 6, 0, 1, 4));

        if (window.ShowDialog() == true)
        {
            await _viewModel.SaveTodoAsync(
                dueDate.SelectedDate ?? date,
                priority.SelectedItem?.ToString() ?? "A",
                (int)progress.Value,
                title.Text,
                description.Text);
        }
    }

    private async Task ShowSearchDialogAsync()
    {
        var window = CreateOwnedDialog("スケジュール検索", 460, 260);
        var root = CreateDialogRoot();
        window.Content = root;

        var start = new DatePicker { SelectedDate = new DateTime(_viewModel.CurrentMonth.Year, 1, 1), IsEnabled = false };
        var end = new DatePicker { SelectedDate = new DateTime(_viewModel.CurrentMonth.Year, 12, 31), IsEnabled = false };
        var regex = new CheckBox { Content = "正規表現で検索する", IsEnabled = false };
        var query = new TextBox();

        root.Children.Add(Labeled("開始日時", start, 0, 0));
        root.Children.Add(Labeled("終了日時", end, 1, 0));
        root.Children.Add(Labeled("検索方法", regex, 2, 0, 1, 4));
        root.Children.Add(Labeled("検索文字列", query, 3, 0, 1, 4));
        root.Children.Add(Place(DialogButtons(window, "　検索開始　", "　キャンセル　"), 4, 0, 1, 4));

        if (window.ShowDialog() == true)
        {
            await ShowEventListDialogAsync("スケジュール一覧", query.Text);
        }
    }

    private async Task ShowEventListDialogAsync(string title, string query)
    {
        var events = string.IsNullOrWhiteSpace(query)
            ? await _viewModel.LoadYearEventsAsync(_viewModel.CurrentMonth)
            : await _viewModel.SearchYearEventsAsync(_viewModel.CurrentMonth, query);

        var window = CreateOwnedDialog(title, 820, 520);
        var panel = new DockPanel { Margin = new Thickness(8) };
        window.Content = panel;

        var status = new StatusBar { Height = 24 };
        status.Items.Add(new TextBlock { Text = $"検索範囲({_viewModel.CurrentMonth.Year}/01/01～{_viewModel.CurrentMonth.Year}/12/31) スケジュール[{events.Count}件]" });
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var close = new Button { Content = "　キャンセル　", MinWidth = 96, Height = 26 };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var grid = new DataGrid
        {
            ItemsSource = new ObservableCollection<CalendarEvent>(events),
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "日時", Binding = new Binding(nameof(CalendarEvent.DateDisplayText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "カレンダー名称", Binding = new Binding(nameof(CalendarEvent.CalendarId)), Width = 160 });
        grid.Columns.Add(new DataGridTextColumn { Header = "件名", Binding = new Binding(nameof(CalendarEvent.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        panel.Children.Add(grid);

        window.ShowDialog();
    }

    private void ShowSettingsDialog()
    {
        var window = CreateOwnedDialog("アプリ設定", 560, 390);
        var panel = new StackPanel { Margin = new Thickness(14) };
        window.Content = panel;

        panel.Children.Add(new TextBlock { Text = "起動時のカレンダー表示タイプ", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new ComboBox { ItemsSource = new[] { "月表示" }, SelectedIndex = 0, IsEnabled = false, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "起動時のタブ表示タイプ", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new ComboBox { ItemsSource = new[] { "カレンダー" }, SelectedIndex = 0, IsEnabled = false, Margin = new Thickness(0, 0, 0, 10) });

        foreach (var text in new[]
        {
            "スケジュールを削除する際に確認ポップアップを表示する",
            "ウインドウの閉じるボタンで、FavGCalSchedulerを終了する",
            "カレンダーのドラッグによるウインドウ移動を行う",
            "スケジュール作成時に終日にチェックを付ける"
        })
        {
            panel.Children.Add(new CheckBox { Content = text, IsChecked = true, IsEnabled = false, Margin = new Thickness(0, 2, 0, 2) });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Google OAuth JSONは下部のカレンダータブで設定できます。詳細設定は後続フェーズで追加します。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12)
        });

        var close = new Button { Content = "OK", Width = 86, Height = 26, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close();
        panel.Children.Add(close);
        window.ShowDialog();
    }

    private static IEnumerable<string> TimeChoices()
    {
        for (var hour = 0; hour < 24; hour++)
        {
            yield return $"{hour:00}:00";
            yield return $"{hour:00}:30";
        }
    }

    private Window CreateOwnedDialog(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Owner = this,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            FontFamily = FontFamily,
            FontSize = FontSize
        };
    }

    private static Grid CreateDialogRoot()
    {
        var grid = new Grid { Margin = new Thickness(12) };
        for (var i = 0; i < 9; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = i == 7 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static FrameworkElement Labeled(string label, FrameworkElement input, int row, int column, int rowSpan = 1, int columnSpan = 2)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 8, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return Place(grid, row, column, rowSpan, columnSpan);
    }

    private static FrameworkElement Place(FrameworkElement element, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }

    private static FrameworkElement DialogButtons(Window window, string okText, string cancelText)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = okText, MinWidth = 96, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = cancelText, MinWidth = 96, Height = 28 };
        ok.Click += (_, _) => window.DialogResult = true;
        cancel.Click += (_, _) => window.DialogResult = false;
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }
}
