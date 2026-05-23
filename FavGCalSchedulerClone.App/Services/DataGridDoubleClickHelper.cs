using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FavGCalSchedulerClone.App.Services;

public static class DataGridDoubleClickHelper
{
    public static bool IsEditableRowDoubleClickTarget(object? originalSource)
    {
        return originalSource is DependencyObject source
            && FindAncestor<DataGridColumnHeader>(source) is null
            && FindAncestor<DataGridRow>(source) is not null;
    }

    public static bool IsEditableRowTypeName(string? typeName)
    {
        return string.Equals(typeName, nameof(DataGridRow), StringComparison.Ordinal)
            || string.Equals(typeName, typeof(DataGridRow).FullName, StringComparison.Ordinal);
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
