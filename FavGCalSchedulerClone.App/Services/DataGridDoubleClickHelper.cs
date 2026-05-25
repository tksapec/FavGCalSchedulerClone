using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FavGCalSchedulerClone.App.Services;

public static class DataGridDoubleClickHelper
{
    public static bool IsEditableRowDoubleClickTarget(object? originalSource)
    {
        return GetRow(originalSource) is not null;
    }

    public static T? GetEditableRowItem<T>(object? originalSource)
        where T : class
    {
        return GetRow(originalSource)?.Item as T;
    }

    public static bool IsEditableRowTypeName(string? typeName)
    {
        return string.Equals(typeName, nameof(DataGridRow), StringComparison.Ordinal)
            || string.Equals(typeName, typeof(DataGridRow).FullName, StringComparison.Ordinal);
    }

    private static DataGridRow? GetRow(object? originalSource)
    {
        if (originalSource is not DependencyObject source
            || FindAncestor<DataGridColumnHeader>(source) is not null)
        {
            return null;
        }

        return FindAncestor<DataGridRow>(source);
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject source)
    {
        if (source is FrameworkContentElement content)
        {
            return content.Parent ?? content.TemplatedParent;
        }

        if (source is Visual or Visual3D)
        {
            var visualParent = VisualTreeHelper.GetParent(source);
            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return LogicalTreeHelper.GetParent(source);
    }
}
