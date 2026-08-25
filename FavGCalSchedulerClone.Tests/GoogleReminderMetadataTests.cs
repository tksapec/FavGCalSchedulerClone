using System.Text.Json;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleReminderMetadataTests
{
    [Theory]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(false, true, false, false, false, true)]
    [InlineData(false, false, true, false, false, true)]
    [InlineData(true, false, false, false, true, true)]
    public void HasEffectiveGoogleReminder_UsesOnlyEffectiveReminderSource(
        bool useDefault,
        bool explicitPopup,
        bool explicitEmail,
        bool defaultPopup,
        bool defaultUnavailable,
        bool expected)
    {
        var metadata = new GoogleReminderMetadata
        {
            UseDefault = useDefault,
            PopupMinutes = explicitPopup ? [30] : [],
            EmailMinutes = explicitEmail ? [60] : [],
            DefaultPopupMinutes = defaultPopup ? [30] : [],
            Source = defaultUnavailable ? "default-unavailable" : null
        };

        Assert.Equal(expected, metadata.HasEffectiveGoogleReminder);
    }

    [Fact]
    public void Clone_CopiesMutableLists()
    {
        var source = new GoogleReminderMetadata
        {
            UseDefault = true,
            PopupMinutes = [10],
            EmailMinutes = [20],
            DefaultPopupMinutes = [30],
            DefaultEmailMinutes = [40],
            AdoptedReminderMinutes = 10,
            AdoptedReminderMethod = "popup",
            Source = "explicit"
        };

        var clone = source.Clone();
        clone.PopupMinutes.Add(11);
        clone.EmailMinutes.Add(21);
        clone.DefaultPopupMinutes.Add(31);
        clone.DefaultEmailMinutes.Add(41);

        Assert.Equal([10], source.PopupMinutes);
        Assert.Equal([20], source.EmailMinutes);
        Assert.Equal([30], source.DefaultPopupMinutes);
        Assert.Equal([40], source.DefaultEmailMinutes);
        Assert.Equal([10, 11], clone.PopupMinutes);
        Assert.Equal([20, 21], clone.EmailMinutes);
        Assert.Equal([30, 31], clone.DefaultPopupMinutes);
        Assert.Equal([40, 41], clone.DefaultEmailMinutes);
        Assert.Equal(source.UseDefault, clone.UseDefault);
        Assert.Equal(source.AdoptedReminderMinutes, clone.AdoptedReminderMinutes);
        Assert.Equal(source.AdoptedReminderMethod, clone.AdoptedReminderMethod);
        Assert.Equal(source.Source, clone.Source);
    }

    [Fact]
    public void Deserialize_NullReminderLists_AreNormalizedToEmptyCollections()
    {
        var metadata = JsonSerializer.Deserialize<GoogleReminderMetadata>("""
            {
              "UseDefault": false,
              "PopupMinutes": null,
              "EmailMinutes": null,
              "DefaultPopupMinutes": null,
              "DefaultEmailMinutes": null
            }
            """);

        Assert.NotNull(metadata);
        Assert.Empty(metadata.PopupMinutes);
        Assert.Empty(metadata.EmailMinutes);
        Assert.Empty(metadata.DefaultPopupMinutes);
        Assert.Empty(metadata.DefaultEmailMinutes);
        Assert.False(metadata.HasGoogleReminder);
        Assert.False(metadata.HasEffectiveGoogleReminder);
        Assert.False(metadata.HasEmailOnly);
        Assert.NotNull(metadata.Clone());
    }
}
