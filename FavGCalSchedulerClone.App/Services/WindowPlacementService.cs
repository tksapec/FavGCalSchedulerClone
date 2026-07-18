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
        var width = Math.Max(minimumWidth, placement.Width);
        var height = Math.Max(minimumHeight, placement.Height);
        if (workingAreas.Count == 0)
        {
            return placement with { Width = width, Height = height };
        }

        var bounds = new Rect(placement.Left, placement.Top, width, height);
        var visibleArea = workingAreas.Any(area => area.IntersectsWith(bounds));
        if (visibleArea)
        {
            return placement with { Width = width, Height = height };
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
