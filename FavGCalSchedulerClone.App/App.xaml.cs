using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace FavGCalSchedulerClone.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\FavGCalSchedulerClone.SingleInstance";
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private Forms.NotifyIcon? _trayIcon;
    private DispatcherTimer? _trayDateTimer;
    private DateTime _trayIconDate;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("FavGCalSchedulerClone は既に起動しています。", "FavGCalSchedulerClone", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsInstanceMutex = true;
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        CreateTrayIcon();

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayDateTimer?.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
        }

        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("表示", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitFromTray());

        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "FavGCalSchedulerClone",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        UpdateTrayDateIcon();

        _trayDateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _trayDateTimer.Tick += (_, _) =>
        {
            if (_trayIconDate != DateTime.Today)
            {
                UpdateTrayDateIcon();
            }
        };
        _trayDateTimer.Start();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is not Window window)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void ExitFromTray()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.ExitFromTray();
        }

        Shutdown();
    }

    private void UpdateTrayDateIcon()
    {
        _trayIconDate = DateTime.Today;
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Drawing.Color.Transparent);
            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(153, 27, 27));
            graphics.FillRoundedRectangle(background, new Drawing.Rectangle(0, 0, 32, 32), 3);
            var text = _trayIconDate.Day.ToString();
            using var format = new Drawing.StringFormat(Drawing.StringFormat.GenericTypographic)
            {
                Alignment = Drawing.StringAlignment.Center,
                LineAlignment = Drawing.StringAlignment.Center,
                FormatFlags = Drawing.StringFormatFlags.NoWrap
            };
            using var font = CreateLargestTrayDateFont(graphics, text, format);
            using var textBrush = new Drawing.SolidBrush(Drawing.Color.White);
            graphics.DrawString(text, font, textBrush, new Drawing.RectangleF(1, 0, 30, 32), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Drawing.Icon.FromHandle(handle);
            var replacement = (Drawing.Icon)icon.Clone();
            var previous = _trayIcon!.Icon;
            _trayIcon.Icon = replacement;
            previous?.Dispose();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Drawing.Font CreateLargestTrayDateFont(
        Drawing.Graphics graphics,
        string text,
        Drawing.StringFormat format)
    {
        for (var size = 31; size >= 8; size--)
        {
            var font = new Drawing.Font("Segoe UI", size, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, int.MaxValue, format);
            if (measured.Width <= 30 && measured.Height <= 30)
            {
                return font;
            }

            font.Dispose();
        }

        return new Drawing.Font("Segoe UI", 8, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal static class DrawingExtensions
{
    public static void FillRoundedRectangle(this Drawing.Graphics graphics, Drawing.Brush brush, Drawing.Rectangle rectangle, int radius)
    {
        using var path = new Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
