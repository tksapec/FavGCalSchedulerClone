namespace FavGCalSchedulerClone.App.Services;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FavGCalSchedulerClone");

    public static string DatabasePath => Path.Combine(AppDataDirectory, "calendar.db");
    public static string TokenDirectory => Path.Combine(AppDataDirectory, "tokens");

    public static void Ensure()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(TokenDirectory);
    }
}
