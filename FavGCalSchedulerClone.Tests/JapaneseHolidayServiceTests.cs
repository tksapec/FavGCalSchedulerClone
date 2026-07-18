using System.Net;
using System.Text;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class JapaneseHolidayServiceTests
{
    [Fact]
    public void ParseCsv_ReadsKnownOfficialHoliday()
    {
        var holidays = JapaneseHolidayService.ParseCsv("\"国民の祝日・休日月日\",\"国民の祝日・休日名称\"\r\n2026/1/1,元日\r\n");

        Assert.Equal("元日", holidays[new DateOnly(2026, 1, 1)]);
    }

    [Fact]
    public async Task UpdateFromOfficialSourceAsync_DecodesShiftJisAndPublishesTheUpdatedHoliday()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "JapaneseHolidays.csv");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var payload = Encoding.GetEncoding(932).GetBytes("2026/1/1,元日\r\n");
        using var client = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, payload));

        try
        {
            Assert.True(await JapaneseHolidayService.UpdateFromOfficialSourceAsync(client, destination, null));
            Assert.Equal("元日", JapaneseHolidayService.GetHolidayName(new DateOnly(2026, 1, 1)));
            Assert.True(File.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateFromOfficialSourceAsync_KeepsExistingHolidaysWhenTheDownloadFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "JapaneseHolidays.csv");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var successfulClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.OK, Encoding.GetEncoding(932).GetBytes("2026/1/1,元日\r\n")));
        using var failedClient = new HttpClient(new StaticResponseHandler(HttpStatusCode.ServiceUnavailable, []));

        try
        {
            Assert.True(await JapaneseHolidayService.UpdateFromOfficialSourceAsync(successfulClient, destination, null));

            Assert.False(await JapaneseHolidayService.UpdateFromOfficialSourceAsync(failedClient, destination, null));

            Assert.Equal("元日", JapaneseHolidayService.GetHolidayName(new DateOnly(2026, 1, 1)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(payload) });
    }
}
