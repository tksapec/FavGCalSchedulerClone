using System.Text;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class FavGCalSchedulerImportAtomicityTests
{
    [Fact]
    public async Task ImportAsync_WhenComparisonFails_DoesNotPersistImportedEvents()
    {
        var sourceFolder = CreateLegacyFolder();
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var service = new FavGCalSchedulerImportService(repository);
            var missingComparisonZip = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.zip");

            await Assert.ThrowsAnyAsync<Exception>(() => service.ImportAsync(new FavGCalImportOptions(
                sourceFolder,
                new Dictionary<string, string> { ["user@example.com"] = "primary" },
                ComparisonZipPath: missingComparisonZip)));

            var events = await repository.LoadEventsAsync(
                new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
                new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeZoneInfo.Local.BaseUtcOffset),
                includeDeleted: true);
            Assert.Empty(events);
        }
        finally
        {
            if (Directory.Exists(sourceFolder))
            {
                Directory.Delete(sourceFolder, recursive: true);
            }

            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    private static string CreateLegacyFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "schedule.ini"), """
            [VERSION]
            version=2.0.1
            [CALENDAR_ITEM]
            count=1
            item0=.\FavSchedule1.favcal
            disp0=1
            """);
        File.WriteAllBytes(Path.Combine(folder, "FavSchedule1.favcal"), CreateFavCalBytes());
        return folder;
    }

    private static byte[] CreateFavCalBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write(Encoding.Unicode.GetBytes("FavSchedule"));
        writer.Write(new byte[32]);
        WriteHeaderString(writer, "Private");
        WriteHeaderString(writer, "https://www.google.com/calendar/feeds/user%40example.com/private/full");
        writer.Write(new byte[32]);

        writer.Write(new byte[] { 0x08, 0x00, 0x01, 0x00 });
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write(0);
        writer.Write(new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeZoneInfo.Local.BaseUtcOffset).ToUnixTimeSeconds());
        writer.Write(new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeZoneInfo.Local.BaseUtcOffset).ToUnixTimeSeconds());
        writer.Write(60);
        writer.Write((ushort)(5 << 8));
        WriteFavString(writer, "Atomic import");
        WriteFavString(writer, "Meeting room");
        WriteFavString(writer, "Body");
        writer.Write(0);
        WriteGoogleId(writer, "legacyevent123");
        return stream.ToArray();
    }

    private static void WriteHeaderString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }

    private static void WriteFavString(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
        writer.Write((ushort)0);
    }

    private static void WriteGoogleId(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
