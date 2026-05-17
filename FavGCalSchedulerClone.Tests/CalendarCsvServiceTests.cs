using System.Text;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarCsvServiceTests
{
    [Fact]
    public async Task ExportAsync_WritesBomAndEscapesFields()
    {
        var csvPath = Path.Combine(CreateTempDirectory(), "events.csv");
        var service = new CalendarCsvService();
        var events = new[]
        {
            new CalendarEvent
            {
                Title = "会議, \"重要\"",
                Description = "1行目\n2行目 #work",
                Location = "東京",
                CalendarId = "primary",
                Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
                End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
                IsAllDay = false
            }
        };

        var result = await service.ExportAsync(events, csvPath);
        var bytes = await File.ReadAllBytesAsync(csvPath);
        var text = await File.ReadAllTextAsync(csvPath, Encoding.UTF8);

        Assert.Equal(1, result.ExportedCount);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        Assert.Contains("\"会議, \"\"重要\"\"\"", text);
        Assert.Contains("\"1行目\n2行目 #work\"", text);
    }

    [Fact]
    public async Task ImportAsync_ReadsNormalAndTodoEvents()
    {
        var csvPath = Path.Combine(CreateTempDirectory(), "events.csv");
        await File.WriteAllTextAsync(csvPath, string.Join(Environment.NewLine,
        [
            string.Join(",", CalendarCsvService.Headers),
            "通常,本文,場所,2026-05-16T09:00:00+09:00,2026-05-16T10:00:00+09:00,false,primary,,#work,,",
            "ToDo,詳細,,2026-05-17T00:00:00+09:00,2026-05-18T00:00:00+09:00,true,primary,,#private,A,56"
        ]), Encoding.UTF8);

        var result = await new CalendarCsvService().ImportAsync(csvPath);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("通常", result.Events[0].Title);
        Assert.Contains("#work", result.Events[0].Description);
        Assert.True(result.Events[1].IsTodoLike);
        Assert.Contains("#todoA56%", result.Events[1].Description);
        Assert.Contains("#private", result.Events[1].Description);
    }

    [Fact]
    public async Task ImportAsync_ReturnsRowErrorsForInvalidRows()
    {
        var csvPath = Path.Combine(CreateTempDirectory(), "invalid.csv");
        await File.WriteAllTextAsync(csvPath, string.Join(Environment.NewLine,
        [
            string.Join(",", CalendarCsvService.Headers),
            ",本文,場所,2026-05-16T09:00:00+09:00,2026-05-16T10:00:00+09:00,false,primary,,,,",
            "日時エラー,本文,場所,invalid,2026-05-16T10:00:00+09:00,false,primary,,,,"
        ]), Encoding.UTF8);

        var result = await new CalendarCsvService().ImportAsync(csvPath);

        Assert.Empty(result.Events);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(2, result.Errors[0].RowNumber);
        Assert.Equal(3, result.Errors[1].RowNumber);
    }

    [Fact]
    public async Task ImportCsvAsync_SavesImportedEventsAsDirty()
    {
        var directory = CreateTempDirectory();
        var dbPath = Path.Combine(directory, "calendar.db");
        var csvPath = Path.Combine(directory, "events.csv");
        await File.WriteAllTextAsync(csvPath, string.Join(Environment.NewLine,
        [
            string.Join(",", CalendarCsvService.Headers),
            "取り込み,本文,,2026-05-16T09:00:00+09:00,2026-05-16T10:00:00+09:00,false,primary,,,,"
        ]), Encoding.UTF8);

        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        var result = await viewModel.ImportCsvAsync(csvPath);
        var events = await repository.LoadDirtyEventsAsync();

        Assert.Single(result.Events);
        Assert.Single(events);
        Assert.Equal("取り込み", events[0].Title);
        Assert.True(events[0].IsDirty);
        Assert.Null(events[0].GoogleEventId);
    }

    [Fact]
    public async Task ExportCurrentYearCsvAsync_ExportsOnlyVisibleYear()
    {
        var directory = CreateTempDirectory();
        var dbPath = Path.Combine(directory, "calendar.db");
        var csvPath = Path.Combine(directory, "events.csv");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.CurrentMonth = new DateTime(2026, 5, 1);
        await repository.SaveEventAsync(EventOn(new DateTime(2026, 5, 16), "in-year"));
        await repository.SaveEventAsync(EventOn(new DateTime(2027, 1, 1), "out-year"));

        var result = await viewModel.ExportCurrentYearCsvAsync(csvPath);
        var text = await File.ReadAllTextAsync(csvPath, Encoding.UTF8);

        Assert.Equal(1, result.ExportedCount);
        Assert.Contains("in-year", text);
        Assert.DoesNotContain("out-year", text);
    }

    private static CalendarEvent EventOn(DateTime date, string title)
    {
        return new CalendarEvent
        {
            Title = title,
            CalendarId = "primary",
            Start = new DateTimeOffset(date),
            End = new DateTimeOffset(date.AddDays(1)),
            IsAllDay = true
        };
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
