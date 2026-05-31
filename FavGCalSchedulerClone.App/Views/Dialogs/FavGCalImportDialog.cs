using System.IO;
using System.Windows;
using System.Windows.Controls;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class FavGCalImportDialog
{
    public static FavGCalImportDialogResult? Show(DialogUiFactory ui, FavGCalImportDialogRequest request)
    {
        var window = ui.CreateOwnedDialog("FavGCalSchedulerデータ移行", 720, 560);
        window.ResizeMode = ResizeMode.CanResize;
        var root = ui.CreateDialogRoot();
        window.Content = root;

        var sourceFolder = new TextBox { Text = request.DefaultSourceFolder };
        var oauthPath = new TextBox { Text = request.OAuthClientJsonPath };
        var comparisonZip = new TextBox { Text = "" };
        var targetCalendar = new ComboBox
        {
            ItemsSource = request.AvailableCalendars,
            DisplayMemberPath = nameof(GoogleCalendarSelectionItem.Summary),
            SelectedValuePath = nameof(GoogleCalendarSelectionItem.Id),
            SelectedValue = request.EditorCalendarId
        };
        var importSettings = new CheckBox { Content = "旧アプリ設定の一部を反映する", IsChecked = true };
        var skipDuplicates = new CheckBox { Content = "重複予定をスキップする", IsChecked = true };
        var repairExistingColors = new CheckBox { Content = "既存予定のラベル色を元データで修復する", IsChecked = false };
        var repairExistingTodoDescriptions = new CheckBox { Content = "既存ToDoの内容を元データで修復する", IsChecked = false };
        var verifyGoogle = new CheckBox { Content = "取り込み前にGoogle予定を取得して照合する", IsChecked = true };
        var analysisText = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        FavGCalImportAnalysis? analysis = null;

        void ShowImportError(string operation, Exception ex)
        {
            var message = $"{operation}に失敗しました。\n\n{ex.Message}";
            analysisText.Text = message;
            MessageBox.Show(window, message, "FavGCalScheduler データ移行エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        var oauthButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var browseOAuth = new Button { Content = "OAuth JSON選択", MinWidth = 120 };
        var authorize = new Button { Content = "Google認証", MinWidth = 110 };
        oauthButtons.Children.Add(browseOAuth);
        oauthButtons.Children.Add(authorize);

        browseOAuth.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Google OAuth client JSON (*.json)|*.json|All files (*.*)|*.*",
                Title = "デバッグ用 OAuth client JSON を選択"
            };
            if (dialog.ShowDialog(window) == true)
            {
                try
                {
                    oauthPath.Text = dialog.FileName;
                    await request.SetOAuthClientJsonPathAsync(dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowImportError("OAuth JSON の設定", ex);
                }
            }
        };
        authorize.Click += async (_, _) =>
        {
            try
            {
                targetCalendar.ItemsSource = await request.AuthorizeGoogleAsync(oauthPath.Text);
            }
            catch (Exception ex)
            {
                ShowImportError("Google 認証", ex);
                return;
            }

            analysisText.Text = "Google認証とカレンダー一覧取得が完了しました。";
        };

        var analyze = new Button { Content = "解析", MinWidth = 96 };
        analyze.Click += async (_, _) =>
        {
            try
            {
                analysis = await request.AnalyzeAsync(sourceFolder.Text);
                analysisText.Text = FormatAnalysis(analysis);
            }
            catch (Exception ex)
            {
                analysis = null;
                ShowImportError("解析", ex);
            }
        };

        root.Children.Add(ui.SectionHeader("移行元"));
        root.Children.Add(ui.WideField("FavGCalSchedulerフォルダ", sourceFolder));
        root.Children.Add(ui.SectionHeader("デバッグ用 Google 連携"));
        root.Children.Add(ui.WideField("OAuth client JSON", oauthPath));
        root.Children.Add(oauthButtons);
        root.Children.Add(ui.SectionHeader("照合"));
        root.Children.Add(ui.WideField("Google エクスポート ZIP", comparisonZip));
        root.Children.Add(ui.SectionHeader("取り込み"));
        root.Children.Add(ui.FormGrid(("既定の取り込み先", targetCalendar, "", analyze)));
        root.Children.Add(importSettings);
        root.Children.Add(skipDuplicates);
        root.Children.Add(repairExistingColors);
        root.Children.Add(repairExistingTodoDescriptions);
        root.Children.Add(verifyGoogle);
        root.Children.Add(ui.WideField("解析結果", analysisText));
        root.Children.Add(ui.DialogButtons(window, "取り込み", "キャンセル"));

        if (window.ShowDialog() != true)
        {
            return null;
        }

        return new FavGCalImportDialogResult(
            sourceFolder.Text,
            oauthPath.Text,
            comparisonZip.Text,
            targetCalendar.SelectedValue?.ToString() ?? request.EditorCalendarId,
            importSettings.IsChecked == true,
            skipDuplicates.IsChecked == true,
            verifyGoogle.IsChecked == true,
            repairExistingColors.IsChecked == true,
            repairExistingTodoDescriptions.IsChecked == true,
            analysis);
    }

    private static string FormatAnalysis(FavGCalImportAnalysis analysis)
    {
        var lines = new List<string>
        {
            $"移行元: {analysis.SourceFolder}",
            $"対象カレンダー: {analysis.Calendars.Count} 件",
            $"検出予定: {analysis.TotalEventCount} 件",
            $"解析エラー: {analysis.ParseErrorCount} 件",
            $"復元不能ToDo: {analysis.UnrestoredTodoCount} 件",
            ""
        };
        lines.AddRange(analysis.Calendars.Select(calendar =>
            $"{Path.GetFileName(calendar.SourcePath)} / {calendar.DisplayName} / {calendar.EventCount} 件 / 旧ID: {calendar.CalendarKey}"));
        if (analysis.Warnings.Count > 0)
        {
            lines.Add("");
            lines.AddRange(analysis.Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record FavGCalImportDialogRequest(
    string DefaultSourceFolder,
    string OAuthClientJsonPath,
    IEnumerable<GoogleCalendarSelectionItem> AvailableCalendars,
    string EditorCalendarId,
    Func<string, Task> SetOAuthClientJsonPathAsync,
    Func<string, Task<IEnumerable<GoogleCalendarSelectionItem>>> AuthorizeGoogleAsync,
    Func<string, Task<FavGCalImportAnalysis>> AnalyzeAsync);

internal sealed record FavGCalImportDialogResult(
    string SourceFolder,
    string OAuthClientJsonPath,
    string ComparisonZipPath,
    string TargetCalendarId,
    bool ImportSettings,
    bool SkipDuplicates,
    bool VerifyGoogleEventsBeforeImport,
    bool RepairExistingColors,
    bool RepairExistingTodoDescriptions,
    FavGCalImportAnalysis? Analysis);
