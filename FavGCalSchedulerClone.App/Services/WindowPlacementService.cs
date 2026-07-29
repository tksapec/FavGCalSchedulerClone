using System.Text.Json;
using System.Windows;

namespace FavGCalSchedulerClone.App.Services;

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized);

public static class WindowPlacementService
{
    public static WindowPlacement? TryLoad(string path, IAppLogger? logger = null)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load window placement.");
            return null;
        }
    }

    public static void Save(string path, WindowPlacement placement, IAppLogger? logger = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) return;

            Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(placement));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save window placement.");
        }
    }

    public static WindowPlacement Normalize(
        WindowPlacement placement,
        double minimumWidth,
        double minimumHeight,
        IReadOnlyList<Rect> workingAreas)
    {
        const double defaultWidth = 1180;
        const double defaultHeight = 720;
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        var width = Finite(placement.Width) && placement.Width > 0 ? Math.Max(minimumWidth, placement.Width) : Math.Max(minimumWidth, defaultWidth);
        var height = Finite(placement.Height) && placement.Height > 0 ? Math.Max(minimumHeight, placement.Height) : Math.Max(minimumHeight, defaultHeight);
        if (workingAreas.Count == 0)
        {
            return placement with { Width = width, Height = height };
        }

        var left = Finite(placement.Left) ? placement.Left : workingAreas[0].Left;
        var top = Finite(placement.Top) ? placement.Top : workingAreas[0].Top;
        var bounds = new Rect(left, top, width, height);
        Rect? target = workingAreas.Cast<Rect?>().FirstOrDefault(area =>
        {
            var intersection = Rect.Intersect(area!.Value, bounds);
            return !intersection.IsEmpty && intersection.Width >= 100 && intersection.Height >= 32;
        });
        if (target is { } visibleWorkArea)
        {
            width = Math.Min(width, visibleWorkArea.Width);
            height = Math.Min(height, visibleWorkArea.Height);
            return placement with
            {
                Left = Math.Clamp(left, visibleWorkArea.Left, visibleWorkArea.Right - Math.Min(100, width)),
                Top = Math.Clamp(top, visibleWorkArea.Top, visibleWorkArea.Bottom - Math.Min(32, height)),
                Width = width,
                Height = height
            };
        }

        var fallback = workingAreas[0];
        width = Math.Min(width, Math.Max(minimumWidth, fallback.Width));
        height = Math.Min(height, Math.Max(minimumHeight, fallback.Height));
        return placement with
        {
            Left = fallback.Left + Math.Max(0, (fallback.Width - width) / 2),
            Top = fallback.Top + Math.Max(0, (fallback.Height - height) / 2),
            Width = width,
            Height = height
        };
    }
}
