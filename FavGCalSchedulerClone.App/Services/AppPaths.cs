namespace FavGCalSchedulerClone.App.Services;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FavGCalSchedulerClone");

    public static string LocalAppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FavGCalSchedulerClone");

    public static string DatabasePath => Path.Combine(AppDataDirectory, "calendar.db");
    public static string TokenDirectory => Path.Combine(AppDataDirectory, "tokens");
    public static string JapaneseHolidayDataPath => Path.Combine(LocalAppDataDirectory, "JapaneseHolidays.csv");
    public static string WindowPlacementPath => Path.Combine(LocalAppDataDirectory, "window-placement.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(TokenDirectory);
    }
}
