using System.Security.Cryptography;
using System.Text;
using Google.Apis.Util.Store;
using Newtonsoft.Json;

namespace FavGCalSchedulerClone.App.Services;

public sealed class ProtectedFileDataStore : IDataStore
{
    private readonly string _folderPath;

    public ProtectedFileDataStore(string folderPath)
    {
        _folderPath = folderPath;
        Directory.CreateDirectory(_folderPath);
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_folderPath))
        {
            foreach (var file in Directory.EnumerateFiles(_folderPath, "*.bin"))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<T?>(default);
        }

        var protectedBytes = File.ReadAllBytes(path);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(bytes);
        return Task.FromResult(JsonConvert.DeserializeObject<T>(json));
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var json = JsonConvert.SerializeObject(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetPath(key), protectedBytes);
        return Task.CompletedTask;
    }

    private string GetPath(string key)
    {
        var safeName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_folderPath, $"{safeName}.bin");
    }
}
