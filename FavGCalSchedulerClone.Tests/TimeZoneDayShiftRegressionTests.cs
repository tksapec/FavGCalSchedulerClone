using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class TimeZoneDayShiftRegressionTests
{
    [Fact]
    public async Task MoveEventAsync_ReevaluatesOffsetWhenNamedZoneCrossesDstBoundary()
    {
        var repository = await CreateRepositoryAsync();
        var original = CreateNewYorkEvent("move-dst");
        await repository.SaveEventAsync(original);
        var viewModel = await CreateViewModelAsync(repository);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        var targetDate = new DateTime(2026, 7, 6);

        var moved = await viewModel.MoveEventAsync(stored, stored.Start.Date, targetDate);

        Assert.True(moved);
        var result = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        Assert.Equal(targetDate, result.Start.Date);
        Assert.Equal(new TimeSpan(9, 0, 0), result.Start.TimeOfDay);
        Assert.Equal(new TimeSpan(10, 0, 0), result.End.TimeOfDay);
        Assert.Equal(TimeSpan.FromHours(-4), result.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), result.End.Offset);
        Assert.Equal("America/New_York", result.StartTimeZoneId);
        Assert.Equal("America/New_York", result.EndTimeZoneId);
    }

    [Fact]
    public async Task PasteEventLabelAsync_ReevaluatesOffsetWhenNamedZoneCrossesDstBoundary()
    {
        var repository = await CreateRepositoryAsync();
        var original = CreateNewYorkEvent("paste-dst");
        await repository.SaveEventAsync(original);
        var viewModel = await CreateViewModelAsync(repository);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(original.Id));
        viewModel.SelectEvent(stored);
        viewModel.CopySelectedEventLabel();
        var targetDate = new DateTime(2026, 7, 6);

        var pasted = await viewModel.PasteEventLabelAsync(targetDate);

        Assert.True(pasted);
        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.FromHours(-4)),
            new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.FromHours(-4)));
        var result = Assert.Single(events, item => item.Id != original.Id);
        Assert.Equal(new TimeSpan(9, 0, 0), result.Start.TimeOfDay);
        Assert.Equal(new TimeSpan(10, 0, 0), result.End.TimeOfDay);
        Assert.Equal(TimeSpan.FromHours(-4), result.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-4), result.End.Offset);
        Assert.Equal("America/New_York", result.StartTimeZoneId);
        Assert.Equal("America/New_York", result.EndTimeZoneId);
    }

    private static CalendarEvent CreateNewYorkEvent(string id) => new()
    {
        Id = id,
        CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
        Title = "New York event",
        Start = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(-5)),
        End = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.FromHours(-5)),
        StartTimeZoneId = "America/New_York",
        EndTimeZoneId = "America/New_York",
        IsDirty = false
    };

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task<MainViewModel> CreateViewModelAsync(CalendarRepository repository)
    {
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return viewModel;
    }
}
