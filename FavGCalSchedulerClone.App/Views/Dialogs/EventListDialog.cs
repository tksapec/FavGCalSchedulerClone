using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class EventListDialog
{
    public static void Show(DialogUiFactory ui, EventListDialogRequest request)
    {
        var eventItems = new ObservableCollection<CalendarEvent>(request.Events);
        var window = ui.CreateOwnedDialog(request.Title, 840, 540);
        var panel = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        window.Content = panel;

        var close = new Button { Content = "閉じる", MinWidth = 96, Height = 28 };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);

        var grid = new DataGrid
        {
            ItemsSource = eventItems,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 24
        };
        grid.MouseDoubleClick += async (_, e) =>
        {
            var calendarEvent = DataGridDoubleClickHelper.GetEditableRowItem<CalendarEvent>(e.OriginalSource);
            if (calendarEvent is null)
            {
                return;
            }

            await request.EditEventAsync(calendarEvent);

            eventItems.Clear();
            foreach (var refreshedEvent in await request.ReloadEventsAsync())
            {
                eventItems.Add(refreshedEvent);
            }
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "日時", Binding = new Binding(nameof(CalendarEvent.DateDisplayText)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "カレンダー", Binding = new Binding(nameof(CalendarEvent.CalendarId)), Width = 150 });
        grid.Columns.Add(ui.CreateColoredTitleColumn(new DataGridLength(1, DataGridLengthUnitType.Star)));
        panel.Children.Add(grid);

        window.ShowDialog();
    }
}

internal sealed record EventListDialogRequest(
    string Title,
    IReadOnlyList<CalendarEvent> Events,
    Func<Task<IReadOnlyList<CalendarEvent>>> ReloadEventsAsync,
    Func<CalendarEvent, Task> EditEventAsync);
