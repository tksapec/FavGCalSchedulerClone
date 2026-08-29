using System.Diagnostics;
using System.Windows;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class DialogAsyncGuard
{
    public static async void Run(Window owner, Func<Task> action, string operation)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{operation}: {ex}");
            MessageBox.Show(
                owner,
                ex.Message,
                $"{operation}エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
