using System.Windows;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow
{
    private void NotifyFirstTrayClose()
    {
        if (_viewModel.HasShownTrayCloseNotification)
        {
            return;
        }

        MessageBox.Show(
            this,
            "このウィンドウを閉じてもアプリは終了せず、通知領域に常駐します。再表示するには通知領域のアイコンをダブルクリックするか、右クリックして「表示」を選択してください。終了するにはアイコンを右クリックして「終了」を選択してください。",
            "通知領域に常駐します",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        _ = SaveTrayCloseNotificationShownAsync();
    }

    private async Task SaveTrayCloseNotificationShownAsync()
    {
        try
        {
            await _viewModel.MarkTrayCloseNotificationShownAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Tray close notification setting could not be saved.");
        }
    }
}
