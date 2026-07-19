namespace FavGCalSchedulerClone.Tests;

public sealed partial class MainWindowMenuTests
{
    private static readonly string MainWindowXamlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App",
        "MainWindow.xaml"));

    [Fact]
    public async Task MainMenu_DoesNotExposePrintOrDisabledItems()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.DoesNotContain("印刷", xaml);
        Assert.DoesNotContain("PrintMenu_Click", xaml);
        Assert.DoesNotContain("PrintPreviewMenu_Click", xaml);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml);
    }

    [Theory]
    [InlineData("F5", "ShowMonthViewCommand")]
    [InlineData("F6", "ShowWeekViewCommand")]
    [InlineData("F7", "ShowDayViewCommand")]
    [InlineData("Home", "TodayCommand")]
    [InlineData("PageUp", "PreviousMonthCommand")]
    [InlineData("PageDown", "NextMonthCommand")]
    [InlineData("F8", "SyncCommand")]
    public async Task MainWindow_DefinesFunctionalKeyBindingForDisplayedShortcut(string key, string command)
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains($"<KeyBinding Key=\"{key}\" Command=\"{{Binding {command}}}\" />", xaml);
    }

    [Fact]
    public async Task MainWindow_DefinesCtrlGForMonthJump()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains("<KeyBinding Key=\"G\" Modifiers=\"Control\" Command=\"{Binding ShowMonthJumpCommand}\" />", xaml);
        Assert.Contains("InputGestureText=\"Ctrl+G\" Command=\"{Binding ShowMonthJumpCommand}\"", xaml);
    }

    [Theory]
    [InlineData("BackupAllCalendarsCommand")]
    [InlineData("RestoreAllCalendarsCommand")]
    [InlineData("ImportFavGCalSchedulerCommand")]
    [InlineData("ImportCsvCommand")]
    [InlineData("ExportCsvCommand")]
    [InlineData("AddScheduleCommand")]
    [InlineData("AddTodoCommand")]
    [InlineData("ShowScheduleListCommand")]
    [InlineData("SearchCommand")]
    [InlineData("ShowSyncDiagnosticsCommand")]
    [InlineData("ShowMonthJumpCommand")]
    [InlineData("ShowSettingsCommand")]
    [InlineData("ShowReminderHistoryCommand")]
    [InlineData("ShowAboutCommand")]
    public async Task MainMenu_UsesCommandBindingsForCommandableItems(string command)
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);
        var viewModelPath = Path.Combine(Path.GetDirectoryName(MainWindowXamlPath)!, "ViewModels", "MainViewModel.cs");
        var viewModel = await File.ReadAllTextAsync(viewModelPath);

        Assert.Contains($"Command=\"{{Binding {command}}}\"", xaml);
        Assert.Contains($"public AsyncRelayCommand {command} {{ get; }}", viewModel);
    }

    [Fact]
    public async Task MainMenu_DoesNotUseClickHandlersForCommandableItems()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);
        var menuItemLines = xaml
            .Split(Environment.NewLine)
            .Where(line => line.Contains("<MenuItem ", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(menuItemLines, line => line.Contains(" Click=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MainWindow_CodeBehindDelegatesEditorDialogs()
    {
        var codeBehindPath = Path.ChangeExtension(MainWindowXamlPath, ".xaml.cs");
        var codeBehind = await File.ReadAllTextAsync(codeBehindPath);

        Assert.Contains("ScheduleEditorDialog.Show(", codeBehind);
        Assert.Contains("TodoEditorDialog.Show(", codeBehind);
        Assert.Contains("SettingsDialog.ShowAsync(", codeBehind);
        Assert.DoesNotContain("AddTodoEditorLayout(", codeBehind);
        Assert.DoesNotContain("CreateColorComboBox(", codeBehind);
        Assert.DoesNotContain("CreateEditorDialogRoot(", codeBehind);
    }

    [Fact]
    public async Task MainWindow_CodeBehindDelegatesRemainingDialogs()
    {
        var codeBehindPath = Path.ChangeExtension(MainWindowXamlPath, ".xaml.cs");
        var codeBehind = await File.ReadAllTextAsync(codeBehindPath);

        Assert.Contains("FavGCalImportDialog.Show(", codeBehind);
        Assert.Contains("SearchDialog.Show(", codeBehind);
        Assert.Contains("EventListDialog.Show(", codeBehind);
        Assert.Contains("RecurrenceScopeDialog.Show(", codeBehind);
        Assert.DoesNotContain("FormatFavGCalAnalysis", codeBehind);
        Assert.DoesNotContain("CreateScopeButton(", codeBehind);
        Assert.DoesNotContain("FormGrid(", codeBehind);
        Assert.DoesNotContain("WideField(", codeBehind);
    }

    [Fact]
    public async Task MainWindow_AsyncVoidHandlersAreGuardedByRunUiActionAsync()
    {
        var codeBehindPath = Path.ChangeExtension(MainWindowXamlPath, ".xaml.cs");
        var codeBehind = await File.ReadAllTextAsync(codeBehindPath);

        Assert.Contains("private async Task RunUiActionAsync(Func<Task> action, string context)", codeBehind);
        Assert.DoesNotContain("private async void ShowSettingsDialog()", codeBehind);
        Assert.Contains("private async Task ShowSettingsDialogAsync()", codeBehind);
        Assert.Contains("await ShowSettingsDialogAsync();", codeBehind);

        foreach (var methodName in new[]
        {
            "DayList_MouseDoubleClick",
            "EventBar_MouseLeftButtonDown",
            "EventSegment_PreviewMouseLeftButtonDown",
            "DayCell_Drop",
            "AddScheduleMenu_Click",
            "AddTodoMenu_Click",
            "ImportFavGCalSchedulerMenu_Click",
            "ScheduleListMenu_Click",
            "SearchMenu_Click",
            "SettingsMenu_Click",
            "ReminderHistoryMenu_Click",
            "SyncMenu_Click",
            "SyncDiagnosticsMenu_Click",
            "DeleteEventButton_Click",
            "SelectedDayEventsGrid_MouseDoubleClick",
            "TodoEventsGrid_MouseDoubleClick",
            "TodoDoneButton_Click",
            "CalendarSelectionMenu_Checked",
            "CalendarSelectionMenu_Unchecked"
        })
        {
            var start = codeBehind.IndexOf($"void {methodName}", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{methodName} was not found.");
            var nextMethod = codeBehind.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
            var body = nextMethod < 0 ? codeBehind[start..] : codeBehind[start..nextMethod];

            Assert.Contains("RunUiActionAsync", body);
        }
    }

    [Fact]
    public async Task MainWindow_DebouncesMeasuredMonthLaneUpdates()
    {
        var codeBehindPath = Path.ChangeExtension(MainWindowXamlPath, ".xaml.cs");
        var codeBehind = await File.ReadAllTextAsync(codeBehindPath);

        Assert.Contains("SizeChanged += MainWindow_SizeChanged", codeBehind);
        Assert.Contains("LayoutUpdated += MainWindow_LayoutUpdated", codeBehind);
        Assert.Contains("TimeSpan.FromMilliseconds(150)", codeBehind);
        Assert.Contains("DayList.ActualHeight", codeBehind);
        Assert.Contains("_viewModel.UpdateMonthLaneCapacity", codeBehind);
        Assert.Contains("Calendar month layout:", codeBehind);
    }

    [Fact]
    public async Task MainWindow_MonthDayCellUsesOneOverlayRegionForEventsAndBadges()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains("<Grid x:Name=\"MonthDayCellLayout\">", xaml);
        Assert.DoesNotContain("<Grid x:Name=\"MonthDayCellLayout\">\r\n                                                <Grid.RowDefinitions>", xaml);
        Assert.DoesNotContain("<ItemsControl Grid.Row=\"1\" Grid.RowSpan=\"2\" ItemsSource=\"{Binding Segments}\"", xaml);
    }

    [Fact]
    public async Task MainWindow_KeepsTheUsableAreaVisibleAndMakesTheSidePanelAdjustable()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains("Height=\"720\" Width=\"1180\" MinHeight=\"600\" MinWidth=\"960\"", xaml);
        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", xaml);
        Assert.Contains("MaxHeight=\"{Binding Source={x:Static SystemParameters.WorkArea}, Path=Height}\"", xaml);
        Assert.Contains("<ColumnDefinition Width=\"6\" />", xaml);
        Assert.Contains("<GridSplitter Grid.Column=\"1\"", xaml);
    }

    [Fact]
    public async Task MainWindow_ShowsTheSelectedDateInTheSidePanelHeader()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains("Text=\"{Binding SelectedDay.Date, StringFormat={}{0:yyyy/MM/dd}}\"", xaml);
    }

    [Fact]
    public async Task MainWindow_UsesACompactMonthHeaderAndReturnsToTodayFromThePeriodTitle()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains("<Grid DockPanel.Dock=\"Top\" Height=\"32\"", xaml);
        Assert.Contains("<RowDefinition Height=\"22\" />", xaml);
        Assert.Contains("Content=\"{Binding CurrentPeriodTitle}\" Command=\"{Binding TodayCommand}\"", xaml);
        Assert.DoesNotContain("Content=\"今日\" Command=\"{Binding TodayCommand}\"", xaml);
        Assert.DoesNotContain("Content=\"移動\" Command=\"{Binding ShowMonthJumpCommand}\"", xaml);
    }

    [Fact]
    public async Task DayCell_WeekendTriggersOverrideOutsideMonthTrigger()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.True(
            xaml.IndexOf("Binding=\"{Binding IsCurrentMonth}\"", StringComparison.Ordinal) <
            xaml.IndexOf("Binding=\"{Binding IsSaturday}\"", StringComparison.Ordinal));
        Assert.True(
            xaml.IndexOf("Binding=\"{Binding IsSaturday}\"", StringComparison.Ordinal) <
            xaml.IndexOf("Binding=\"{Binding IsWorkdayOverride}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MonthView_ShowsAWeekNumberColumnWithoutChangingTheSevenDayGrid()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains("<ColumnDefinition Width=\"30\" />", xaml);
        Assert.Contains("ItemsSource=\"{Binding MonthWeekNumbers}\"", xaml);
        Assert.Contains("Grid.Column=\"1\" ItemsSource=\"{Binding WeekdayHeaders}\"", xaml);
        Assert.Contains("Grid.Row=\"1\" Grid.Column=\"1\" ItemsSource=\"{Binding VisibleCalendarDays}\"", xaml);
    }

    [Fact]
    public async Task SettingsDialog_OffersAnExplicitJapaneseHolidayRefresh()
    {
        var settingsPath = Path.Combine(Path.GetDirectoryName(MainWindowXamlPath)!, "Views", "Dialogs", "SettingsDialog.cs");
        var settings = await File.ReadAllTextAsync(settingsPath);

        Assert.Contains("Content = \"祝日データをオンライン更新\"", settings);
        Assert.Contains("UpdateJapaneseHolidaysAsync", settings);
    }

}
