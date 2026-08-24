using FavGCalSchedulerClone.App.Services;
using System.Net;
using System.Net.Http;
using System.Text;

namespace FavGCalSchedulerClone.Tests;

public sealed class JapaneseHolidayServiceTests
{
    [Fact]
    public async Task Project_PublishesTheBundledHolidayCsv()
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App",
            "FavGCalSchedulerClone.App.csproj"));

        var project = await File.ReadAllTextAsync(projectPath);

        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", project);
    }

    [Fact]
    public async Task UpdateFromOfficialSourceAsync_DecodesShiftJisAndWritesTheLocalOverride()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "JapaneseHolidays.csv");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var payload = Encoding.GetEncoding(932).GetBytes("2026/1/1,元日\r\n");
        using var client = new HttpClient(new StaticResponseHandler(payload));

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
    public void ParseCsv_ReadsOfficialAndSubstituteHolidays()
    {
        const string csv = "\"国民の祝日・休日月日\",\"国民の祝日・休日名称\"\r\n2026/1/1,元日\r\n2026/5/6,休日\r\n";

        var holidays = JapaneseHolidayService.ParseCsv(csv);

        Assert.Equal("元日", holidays[new DateOnly(2026, 1, 1)]);
        Assert.Equal("休日", holidays[new DateOnly(2026, 5, 6)]);
    }

    [Fact]
    public void ParseCsv_IgnoresInvalidRows()
    {
        const string csv = "date,name\r\nnot-a-date,invalid\r\n2026/9/23,秋分の日\r\n";

        var holidays = JapaneseHolidayService.ParseCsv(csv);

        Assert.Single(holidays);
        Assert.Equal("秋分の日", holidays[new DateOnly(2026, 9, 23)]);
    }

    private sealed class StaticResponseHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }
}
