using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.ViewModels;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class DialogUiFactoryTests
{
    [Fact]
    public void EnsureSelectedColorOption_AddsDisabledColorUsedByExistingEvent()
    {
        var options = new[]
        {
            new EventColorSelectionItem(null, "標準（白）", "#FFFFFF", "#111111")
        };

        var result = DialogUiFactory.EnsureSelectedColorOption(options, "5");

        var selected = Assert.Single(result, item => item.Id == "5");
        Assert.Contains("現在の予定", selected.Label);
    }

    [Fact]
    public void EnsureSelectedColorOption_DoesNotDuplicateAnAvailableColor()
    {
        var options = new[]
        {
            new EventColorSelectionItem(null, "標準（白）", "#FFFFFF", "#111111"),
            new EventColorSelectionItem("5", "色 5", "#FBD75B", "#111111")
        };

        var result = DialogUiFactory.EnsureSelectedColorOption(options, "5");

        Assert.Single(result, item => item.Id == "5");
    }

    [Fact]
    public async Task SharedDialogButtons_SupportEnterAndEscapeKeyboardActions()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            GetRepositoryRoot(),
            "FavGCalSchedulerClone.App",
            "Views",
            "Dialogs",
            "DialogUiFactory.cs"));

        Assert.Contains("IsDefault = true", source, StringComparison.Ordinal);
        Assert.Contains("IsCancel = true", source, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FavGCalSchedulerClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
