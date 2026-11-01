using System.Windows;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal sealed class UnsavedChangesCloseGuard
{
    private readonly Window _window;
    private readonly Func<bool> _hasChanges;
    private bool _allowClosing;

    public UnsavedChangesCloseGuard(Window window, Func<bool> hasChanges)
    {
        _window = window;
        _hasChanges = hasChanges;
        _window.Closing += Window_Closing;
    }

    public void Cancel()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        _allowClosing = true;
        _window.DialogResult = false;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_window.DialogResult == true || _allowClosing || !_hasChanges())
        {
            return;
        }

        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        _allowClosing = true;
    }

    private bool ConfirmDiscard() =>
        !_hasChanges()
        || MessageBox.Show(
            _window,
            "入力内容を破棄して閉じますか。",
            "未保存の変更",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
}
