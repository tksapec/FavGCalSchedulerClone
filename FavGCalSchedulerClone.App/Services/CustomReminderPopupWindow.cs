using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal sealed class CustomReminderPopupWindow : Window
{
    private const double PopupWidth = 340;
    private const double PopupHeight = 156;
    private const double MarginFromEdge = 16;
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly Func<int, Task> _snoozeAsync;
    private bool _closingAnimated;

    private CustomReminderPopupWindow(ReminderNotification notification, Func<int, Task> snoozeAsync)
    {
        _snoozeAsync = snoozeAsync;
        Width = PopupWidth;
        Height = PopupHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Opacity = 0;
        Content = CreateContent(notification);

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
        _autoCloseTimer.Tick += (_, _) => CloseWithAnimation();
    }

    public static Task ShowAsync(
        Window owner,
        ReminderNotification notification,
        Func<int, Task> snoozeAsync,
        CancellationToken cancellationToken = default)
    {
        return owner.Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var popup = new CustomReminderPopupWindow(notification, snoozeAsync)
            {
                Owner = owner
            };
            popup.Show();
        }).Task;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - MarginFromEdge;
        var finalTop = area.Bottom - Height - MarginFromEdge;
        Top = area.Bottom + 4;

        BeginAnimation(TopProperty, new DoubleAnimation(finalTop, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        _autoCloseTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
    }

    private Border CreateContent(ReminderNotification notification)
    {
        var accent = notification.IsTodoLike ? Brushes.DarkGreen : Brushes.SteelBlue;
        var root = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.25
            }
        };

        var grid = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = grid;

        var header = new DockPanel();
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var close = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => CloseWithAnimation();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);

        header.Children.Add(new TextBlock
        {
            Text = notification.IsTodoLike ? "ToDo通知" : "予定通知",
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        });

        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 8) };
        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        body.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(notification.Title) ? "(no title)" : notification.Title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14
        });
        body.Children.Add(new TextBlock
        {
            Text = notification.DateDisplayText,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 5, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        buttons.Children.Add(CreateSnoozeButton("5分後", 5));
        buttons.Children.Add(CreateSnoozeButton("10分後", 10));

        return root;
    }

    private Button CreateSnoozeButton(string text, int minutes)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 72,
            Height = 28,
            Margin = new Thickness(6, 0, 0, 0)
        };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            await _snoozeAsync(minutes);
            CloseWithAnimation();
        };
        return button;
    }

    private void CloseWithAnimation()
    {
        if (_closingAnimated)
        {
            return;
        }

        _closingAnimated = true;
        _autoCloseTimer.Stop();
        var area = SystemParameters.WorkArea;
        var animation = new DoubleAnimation(area.Bottom + 4, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) => Close();
        BeginAnimation(TopProperty, animation);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
    }
}
