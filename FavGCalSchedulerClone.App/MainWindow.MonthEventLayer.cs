using System.Windows;
using System.Windows.Input;
using FavGCalSchedulerClone.App.Controls;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App;

public partial class MainWindow
{
    private async void MonthEventLayer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        await RunUiActionAsync(async () =>
        {
            if (sender is not MonthEventLayer layer
                || layer.HitTestSegment(e.GetPosition(layer)) is not { Event: not null } segment)
            {
                return;
            }

            if (e.ClickCount >= 2)
            {
                _dragStartPoint = null;
                _dragSegment = null;
                _viewModel.SelectEventSegment(segment);
                e.Handled = true;
                await OpenSelectedEventEditorAsync();
                return;
            }

            _dragStartPoint = e.GetPosition(this);
            _dragSegment = segment;
        }, nameof(MonthEventLayer_PreviewMouseLeftButtonDown));
    }

    private void MonthEventLayer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not MonthEventLayer layer
            || _dragStartPoint is not Point startPoint
            || _dragSegment is not { Event: not null } segment
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStartPoint = null;
        _dragSegment = null;
        _viewModel.SelectEventSegment(segment);
        using (_applicationInteractionGuard.EnterDragOperation())
        {
            DragDrop.DoDragDrop(layer, segment, DragDropEffects.Move);
        }

        ClearDragTarget();
    }

    private void MonthEventLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MonthEventLayer layer
            || e.ClickCount >= 2
            || layer.HitTestSegment(e.GetPosition(layer)) is not { Event: not null } segment)
        {
            return;
        }

        _viewModel.SelectEventSegment(segment);
        e.Handled = true;
    }

    private void MonthEventLayer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not MonthEventLayer layer
            || layer.HitTestSegment(e.GetPosition(layer)) is not { Event: not null } segment)
        {
            return;
        }

        _viewModel.SelectEventSegment(segment);
        e.Handled = true;
        ShowCalendarContextMenu(layer);
    }
}
