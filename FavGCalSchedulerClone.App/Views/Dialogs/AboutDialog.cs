using System.Windows;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class AboutDialog
{
    public static void Show(Window owner)
    {
        MessageBox.Show(
            owner,
            "FavGCalSchedulerClone\nVersion 0.1.0",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
