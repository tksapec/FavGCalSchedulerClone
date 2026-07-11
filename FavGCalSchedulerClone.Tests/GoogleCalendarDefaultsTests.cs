using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using System.Net;
using System.Text;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleCalendarDefaultsTests
{
    [Fact]
    public void CalendarScopes_IncludeEventSyncAndCalendarListReadScopes()
    {
        Assert.Contains(GoogleCalendarDefaults.CalendarEventsScope, GoogleCalendarDefaults.CalendarScopes);
        Assert.Contains(GoogleCalendarDefaults.CalendarListReadonlyScope, GoogleCalendarDefaults.CalendarScopes);
    }

    [Fact]
    public async Task ListCalendarsAsync_LoadsEveryPageAndPreservesLaterPageDefaultReminders()
    {
        using var service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientFactory = new CalendarListHttpClientFactory()
        });
        IGoogleCalendarClient client = new GoogleCalendarApi.GoogleCalendarClient(service);

        var calendars = await client.ListCalendarsAsync();

        Assert.Collection(
            calendars,
            calendar => Assert.Equal("Alpha", calendar.Summary),
            calendar =>
            {
                Assert.Equal("Later calendar", calendar.Summary);
                var reminder = Assert.Single(calendar.DefaultReminders!);
                Assert.Equal("popup", reminder.Method);
                Assert.Equal(30, reminder.Minutes);
            },
            calendar => Assert.Equal("Zulu", calendar.Summary));
        Assert.Equal(1, calendars.Count(calendar => calendar.Id == "duplicate"));
    }

    private sealed class CalendarListHttpClientFactory : IHttpClientFactory
    {
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
        {
            return new ConfigurableHttpClient(new ConfigurableMessageHandler(new CalendarListHttpMessageHandler()));
        }
    }

    private sealed class CalendarListHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pageToken = request.RequestUri!.Query.Contains("pageToken=page-2", StringComparison.Ordinal)
                ? "page-2"
                : null;
            var json = pageToken is null
                ? """
                  { "nextPageToken": "page-2", "items": [
                    { "id": "zulu", "summary": "Zulu" },
                    { "id": "duplicate", "summary": "Alpha" }
                  ] }
                  """
                : """
                  { "items": [
                    { "id": "later", "summary": "Later calendar", "defaultReminders": [ { "method": "popup", "minutes": 30 } ] },
                    { "id": "duplicate", "summary": "Duplicate" }
                  ] }
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
