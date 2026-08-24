namespace FavGCalSchedulerClone.Tests;

public sealed class ProtectedFileDataStoreSourceTests
{
    private static readonly string SourcePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App", "Services", "ProtectedFileDataStore.cs"));

    [Fact]
    public async Task StoreAsync_FlushesTemporaryFileBeforeAtomicReplacement()
    {
        var source = await File.ReadAllTextAsync(SourcePath);

        Assert.Contains("FileOptions.WriteThrough", source);
        Assert.Contains("Flush(flushToDisk: true)", source);
        Assert.Contains("File.Move(tempPath, path, overwrite: true)", source);
        Assert.DoesNotContain("File.WriteAllBytes(GetPath(key), protectedBytes)", source);
    }
}
