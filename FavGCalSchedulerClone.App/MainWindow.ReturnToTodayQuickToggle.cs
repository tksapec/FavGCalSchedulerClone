using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow
{
    private const string ReturnToTodayQuickToggleHeader = "フォーカス解除時に今日へ戻す(_T)";
    private MenuItem? _returnToTodayQuickToggleMenuItem;
    private PropertyChangedEventHandler? _returnToTodayQuickTogglePropertyChangedHandler;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoadedForReturnToTodayQuickToggle));
    }

    private static void OnLoadedForReturnToTodayQuickToggle(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.EnsureReturnToTodayQuickToggle();
        }
    }

    private void EnsureReturnToTodayQuickToggle()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (_returnToTodayQuickToggleMenuItem is null)
        {
            if (Content is not DockPanel root)
            {
                return;
            }

            var menu = root.Children.OfType<Menu>().FirstOrDefault();
            var settingsMenu = menu?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "設定(_C)", StringComparison.Ordinal));
            if (settingsMenu is null)
            {
                return;
            }

            _returnToTodayQuickToggleMenuItem = settingsMenu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), ReturnToTodayQuickToggleHeader, StringComparison.Ordinal));

            if (_returnToTodayQuickToggleMenuItem is null)
            {
                _returnToTodayQuickToggleMenuItem = new MenuItem
                {
                    Header = "フォーカス解除時に今日へ戻す(_T)",
                    IsCheckable = true,
                    IsChecked = viewModel.ReturnToTodayWhenDeactivated,
                    Command = viewModel.ToggleReturnToTodayWhenDeactivatedCommand,
                    ToolTip = "ONの場合、他のアプリへ切り替えたとき選択日を今日へ戻します"
                };

                var insertIndex = Math.Min(1, settingsMenu.Items.Count);
                settingsMenu.Items.Insert(insertIndex, _returnToTodayQuickToggleMenuItem);
                settingsMenu.Items.Insert(Math.Min(insertIndex + 1, settingsMenu.Items.Count), new Separator());
            }
        }

        _returnToTodayQuickToggleMenuItem.Command = viewModel.ToggleReturnToTodayWhenDeactivatedCommand;
        _returnToTodayQuickToggleMenuItem.IsChecked = viewModel.ReturnToTodayWhenDeactivated;

        if (_returnToTodayQuickTogglePropertyChangedHandler is null)
        {
            _returnToTodayQuickTogglePropertyChangedHandler = (_, _) =>
            {
                if (_returnToTodayQuickToggleMenuItem is not null)
                {
                    _returnToTodayQuickToggleMenuItem.IsChecked = viewModel.ReturnToTodayWhenDeactivated;
                }
            };
            viewModel.PropertyChanged += _returnToTodayQuickTogglePropertyChangedHandler;
        }
    }
}
