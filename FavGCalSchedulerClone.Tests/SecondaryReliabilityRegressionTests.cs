using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class SecondaryReliabilityRegressionTests
{
    [Fact]
    public void ExpandForRange_MalformedRecurringMaster_PreservesSourceOccurrenceForRepair()
    {
        var master = new CalendarEvent
        {
            Id = "malformed-series",
            Title = "Repair me",
            Start = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=NOTSUPPORTED\"]"
        };

        var results = RecurrenceExpansionService.ExpandForRange(
            [master],
            new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero));

        var visible = Assert.Single(results);
        Assert.Equal("malformed-series", visible.Id);
        Assert.False(visible.IsGeneratedOccurrence);
        Assert.Equal(master.Start, visible.Start);
        Assert.Equal(master.End, visible.End);
    }

    [Fact]
    public async Task CsvRoundTrip_LiteralApostropheBeforeFormulaPrefix_PreservesExactValue()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), $"csv-roundtrip-{Guid.NewGuid():N}.csv");
        try
        {
            var service = new CalendarCsvService();
            var original = new CalendarEvent
            {
                Id = "csv-literal",
                Title = "'=literal text",
                Start = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero),
                End = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)
            };

            await service.ExportAsync([original], csvPath);
            var imported = await service.ImportAsync(csvPath);

            Assert.Empty(imported.Errors);
            var importedEvent = Assert.Single(imported.Events);
            Assert.Equal(original.Title, importedEvent.Title);
        }
        finally
        {
            if (File.Exists(csvPath))
            {
                File.Delete(csvPath);
            }
        }
    }
}
