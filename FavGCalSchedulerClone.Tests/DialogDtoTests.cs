using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class DialogDtoTests
{
    [Fact]
    public void ScheduleEditorResult_PreservesEditorValues()
    {
        var result = new ScheduleEditorResult(
            "primary",
            "7",
            new DateTime(2026, 5, 22),
            new DateTime(2026, 5, 23),
            "10:00",
            "12:30",
            true,
            10,
            true,
            false,
            "C202a",
            "水田C 富士電機来社対応",
            "2026/05/12 14:12\r\nAN-40C 予備検証試験");

        Assert.Equal("primary", result.CalendarId);
        Assert.Equal("7", result.ColorId);
        Assert.Equal(new DateTime(2026, 5, 22), result.StartDate);
        Assert.Equal(new DateTime(2026, 5, 23), result.EndDate);
        Assert.Equal("10:00", result.StartTime);
        Assert.Equal("12:30", result.EndTime);
        Assert.True(result.IsAllDay);
        Assert.Equal(10, result.ReminderMinutesBeforeStart);
        Assert.True(result.IsAppReminderEnabled);
        Assert.False(result.IsGoogleEmailReminderEnabled);
        Assert.Equal("C202a", result.Location);
        Assert.Equal("水田C 富士電機来社対応", result.Title);
        Assert.Equal("2026/05/12 14:12\r\nAN-40C 予備検証試験", result.Description);
    }

    [Fact]
    public void ScheduleEditorRequest_PreservesGoogleEmailReminderDisplayText()
    {
        var request = new ScheduleEditorRequest(
            true,
            new DateTime(2026, 5, 22),
            new DateTime(2026, 5, 22),
            "09:00",
            "10:00",
            false,
            10,
            false,
            true,
            "A101",
            "primary",
            null,
            "meeting",
            "body",
            [],
            [],
            [],
            [new ReminderOption("10分前", 10)],
            "Googleメール通知: 10分前");

        Assert.Equal(10, request.ReminderMinutesBeforeStart);
        Assert.False(request.IsAppReminderEnabled);
        Assert.True(request.IsGoogleEmailReminderEnabled);
        Assert.Equal("Googleメール通知: 10分前", request.GoogleEmailReminderDisplayText);
    }

    [Fact]
    public void TodoEditorResult_PreservesMetadataAndBodyNewlines()
    {
        var body = "2026/05/20 11:40\r\n\r\n廣田s：2026/05/20 12:31 江島s";
        var result = new TodoEditorResult(
            "primary",
            "5",
            new DateTime(2026, 5, 20),
            "A",
            60,
            "国際営業部 内田s 贈り物賛同者連絡",
            body);

        Assert.Equal("primary", result.CalendarId);
        Assert.Equal("5", result.ColorId);
        Assert.Equal(new DateTime(2026, 5, 20), result.DueDate);
        Assert.Equal("A", result.Priority);
        Assert.Equal(60, result.Progress);
        Assert.Equal("国際営業部 内田s 贈り物賛同者連絡", result.Title);
        Assert.Equal(body, result.Description);
    }

    [Fact]
    public void TodoEditorDialog_UsesScheduleSizedDueDateColumn()
    {
        Assert.True(TodoEditorDialog.DueDateColumnPhysicalWidth >= 230);
        Assert.True(TodoEditorDialog.UpperDueColumnWeight > 2);
    }

    [Fact]
    public async Task ScheduleAndTodoEditors_AreResizableAndUseScreenFittingOwnedDialogs()
    {
        var scheduleCode = await File.ReadAllTextAsync(DialogSourcePath("ScheduleEditorDialog.cs"));
        var todoCode = await File.ReadAllTextAsync(DialogSourcePath("TodoEditorDialog.cs"));

        Assert.Contains("ResizeMode.CanResize", scheduleCode);
        Assert.Contains("ResizeMode.CanResize", todoCode);
        Assert.Contains("fitToWorkArea: true", scheduleCode);
        Assert.Contains("fitToWorkArea: true", todoCode);
    }

    [Fact]
    public async Task ScheduleAndTodoEditors_UseSmallerMinimumSizesThanInitialSizes()
    {
        var scheduleCode = await File.ReadAllTextAsync(DialogSourcePath("ScheduleEditorDialog.cs"));
        var todoCode = await File.ReadAllTextAsync(DialogSourcePath("TodoEditorDialog.cs"));

        Assert.Contains("ScheduleMinHeightPhysical", scheduleCode);
        Assert.Contains("TodoMinHeightPhysical", todoCode);
        Assert.DoesNotContain("MinHeight = ui.Y(255)", scheduleCode);
        Assert.DoesNotContain("MinHeight = ui.Y(145)", todoCode);
    }

    [Fact]
    public async Task ScheduleEditor_UsesScrollableFormAndKeepsButtonsOutsideTheScrollArea()
    {
        var scheduleCode = await File.ReadAllTextAsync(DialogSourcePath("ScheduleEditorDialog.cs"));

        Assert.Contains("var formScrollViewer = new ScrollViewer", scheduleCode);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", scheduleCode);
        Assert.Contains("Content = form", scheduleCode);
        Assert.Contains("ScheduleDescriptionMinHeightPhysical", scheduleCode);
        Assert.Contains("MinHeight = ui.Y(ScheduleDescriptionMinHeightPhysical)", scheduleCode);
        Assert.Contains("Grid.SetRow(buttons, 2)", scheduleCode);
        Assert.Contains("root.Children.Add(formScrollViewer)", scheduleCode);
        Assert.True(scheduleCode.IndexOf("root.Children.Add(formScrollViewer)", StringComparison.Ordinal)
                    < scheduleCode.IndexOf("root.Children.Add(buttons)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScheduleEditor_UsesTheSharedEditableHistoryComboBoxForLocationAndTitle()
    {
        var scheduleCode = await File.ReadAllTextAsync(DialogSourcePath("ScheduleEditorDialog.cs"));

        Assert.Contains("CreateEditableHistoryComboBox(request.Location, request.LocationHistory)", scheduleCode);
        Assert.Contains("CreateEditableHistoryComboBox(request.Title, request.TitleHistory)", scheduleCode);
    }

    [Fact]
    public async Task EditableHistoryComboBoxBehavior_UsesWpfStandardTextEditingOnly()
    {
        var behaviorPath = Path.Combine(
            Path.GetDirectoryName(DialogSourcePath("ScheduleEditorDialog.cs"))!,
            "EditableHistoryComboBoxBehavior.cs");
        var behaviorCode = await File.ReadAllTextAsync(behaviorPath);

        Assert.Contains("PART_EditableTextBox", behaviorCode);
        Assert.Contains("IsEditable = true", behaviorCode);
        Assert.Contains("IsTextSearchEnabled = true", behaviorCode);
        Assert.DoesNotContain("PreviewKeyDown", behaviorCode);
        Assert.DoesNotContain("KeyBinding", behaviorCode);
        Assert.DoesNotContain("CommandBinding", behaviorCode);
        Assert.DoesNotContain("Clipboard", behaviorCode);
        Assert.DoesNotContain("ContextMenu", behaviorCode);
    }

    [Fact]
    public async Task SyncDiagnosticsDialog_ShowsDirtyItemsTab()
    {
        var dialogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App",
            "Views",
            "Dialogs",
            "SyncDialogs.cs"));
        var code = await File.ReadAllTextAsync(dialogPath);

        Assert.Contains("diagnostics.DirtyItems", code);
        Assert.Contains("nameof(SyncDirtyItem.Kind)", code);
        Assert.Contains("nameof(SyncDirtyItem.Operation)", code);
        Assert.Contains("未同期", code);
    }

    [Fact]
    public async Task SyncDiagnosticsDialog_ClearButtonExplainsDirtyItemsRemain()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("SyncDialogs.cs"));

        Assert.Contains("未同期データは削除されません", code);
    }

    [Fact]
    public async Task SyncPreviewDialog_ShowsFieldDiffDetails()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("SyncDialogs.cs"));

        Assert.Contains("FieldDiffs", code);
        Assert.Contains("nameof(SyncFieldDiff.DisplayName)", code);
        Assert.Contains("nameof(SyncFieldDiff.LocalValue)", code);
        Assert.Contains("nameof(SyncFieldDiff.GoogleValue)", code);
    }

    [Fact]
    public async Task ReminderHistoryDialog_IsNotificationCenterWithRefreshGoogleReminderAction()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("ReminderHistoryDialog.cs"));

        Assert.Contains("refreshGoogleRemindersAsync", code);
        Assert.Contains("GooglePopupReminderText", code);
        Assert.Contains("GoogleEmailReminderText", code);
        Assert.Contains("ReminderDifferenceText", code);
    }

    [Fact]
    public async Task EventListDialog_ExposesBulkEditAndDeleteActions()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("EventListDialog.cs"));

        Assert.Contains("BulkEditAsync", code);
        Assert.Contains("BulkDeleteAsync", code);
        Assert.Contains("SelectionMode = DataGridSelectionMode.Extended", code);
        Assert.Contains("BulkEventUpdateDialog", code);
    }

    [Fact]
    public void SettingsDialogResult_PreservesSettingsAndOAuthPath()
    {
        var settings = new AppSettings
        {
            StartupCalendarViewMode = CalendarViewMode.Week,
            StartupTodoTabIndex = 1,
            WeekStartsOnMonday = true,
            ShowSyncPreviewBeforeManualSync = true,
            EnableSyncDiagnostics = true,
            SyncConflictPolicy = SyncConflictPolicy.PreferLocal
        };

        var result = new SettingsDialogResult(settings, @"C:\temp\client_secret.json");

        Assert.Same(settings, result.Settings);
        Assert.Equal(CalendarViewMode.Week, result.Settings.StartupCalendarViewMode);
        Assert.Equal(1, result.Settings.StartupTodoTabIndex);
        Assert.True(result.Settings.WeekStartsOnMonday);
        Assert.True(result.Settings.ShowSyncPreviewBeforeManualSync);
        Assert.True(result.Settings.EnableSyncDiagnostics);
        Assert.Equal(SyncConflictPolicy.PreferLocal, result.Settings.SyncConflictPolicy);
        Assert.Equal(@"C:\temp\client_secret.json", result.OAuthClientJsonPath);
    }

    [Fact]
    public void FavGCalImportDialogResult_PreservesImportSelections()
    {
        var analysis = new FavGCalImportAnalysis(
            @"C:\Users\user\Documents\FavGCalScheduler",
            [new FavGCalSourceCalendar(@"C:\data\work.favcal", "legacy-work", "Work", null) { EventCount = 12 }],
            12,
            1,
            2,
            ["warning"]);

        var result = new FavGCalImportDialogResult(
            analysis.SourceFolder,
            @"C:\oauth\client.json",
            @"C:\compare\google.zip",
            "primary",
            ImportSettings: true,
            SkipDuplicates: false,
            VerifyGoogleEventsBeforeImport: true,
            RepairExistingColors: true,
            RepairExistingTodoDescriptions: true,
            analysis);

        Assert.Equal(analysis.SourceFolder, result.SourceFolder);
        Assert.Equal(@"C:\oauth\client.json", result.OAuthClientJsonPath);
        Assert.Equal(@"C:\compare\google.zip", result.ComparisonZipPath);
        Assert.Equal("primary", result.TargetCalendarId);
        Assert.True(result.ImportSettings);
        Assert.False(result.SkipDuplicates);
        Assert.True(result.VerifyGoogleEventsBeforeImport);
        Assert.True(result.RepairExistingColors);
        Assert.True(result.RepairExistingTodoDescriptions);
        Assert.Same(analysis, result.Analysis);
    }

    [Fact]
    public void SearchDialogResult_PreservesQuery()
    {
        var result = new SearchDialogResult("NHP来日");

        Assert.Equal("NHP来日", result.Query);
    }

    [Theory]
    [InlineData(RecurrenceEditScope.ThisOccurrence)]
    [InlineData(RecurrenceEditScope.ThisAndFollowing)]
    [InlineData(RecurrenceEditScope.AllEvents)]
    public void RecurrenceScopeDialogRequest_CanRepresentScopeDialogMode(RecurrenceEditScope scope)
    {
        var editRequest = new RecurrenceScopeDialogRequest(IsDelete: false);
        var deleteRequest = new RecurrenceScopeDialogRequest(IsDelete: true);

        Assert.False(editRequest.IsDelete);
        Assert.True(deleteRequest.IsDelete);
        Assert.Contains(scope, Enum.GetValues<RecurrenceEditScope>());
    }

    private static string DialogSourcePath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App",
            "Views",
            "Dialogs",
            fileName));
    }
}
