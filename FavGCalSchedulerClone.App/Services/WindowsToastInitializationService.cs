using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FavGCalSchedulerClone.App.Services;

public sealed class WindowsToastInitializationService
{
    public const string AppUserModelId = "tksapec.FavGCalSchedulerClone";
    private const string ShortcutName = "FavGCalSchedulerClone.lnk";
    private const string LastStatusSettingKey = "toast:last-status";
    private readonly CalendarRepository _repository;
    private WindowsToastStatus _status = WindowsToastStatus.NotInitialized("Toast notifications have not been initialized.");

    public WindowsToastInitializationService(CalendarRepository repository)
    {
        _repository = repository;
    }

    public WindowsToastStatus CurrentStatus => _status;
    public string CurrentExecutablePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

    public bool IsCurrentToastVerificationValid(Models.AppSettings settings)
    {
        return settings.ToastVerifiedAt is not null
            && string.Equals(settings.ToastVerifiedAumid, AppUserModelId, StringComparison.Ordinal)
            && string.Equals(settings.ToastVerifiedExecutablePath, CurrentExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WindowsToastStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return await SaveStatusAsync(WindowsToastStatus.Failed("Windows toast notifications are only available on Windows."), cancellationToken);
        }

        if (IsProcessElevated())
        {
            return await SaveStatusAsync(WindowsToastStatus.Failed("Windows toast notifications are not supported while the app is running as administrator."), cancellationToken);
        }

        try
        {
            var exePath = CurrentExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return await SaveStatusAsync(WindowsToastStatus.Failed("The application executable path could not be resolved."), cancellationToken);
            }

            var shortcutPath = GetShortcutPath();
            InstallShortcut(shortcutPath, exePath, AppUserModelId);
            DesktopNotificationManagerCompat.RegisterAumidAndComServer<WindowsToastNotificationActivator>(AppUserModelId);
            DesktopNotificationManagerCompat.RegisterActivator<WindowsToastNotificationActivator>();
            _ = DesktopNotificationManagerCompat.CreateToastNotifier();
            return await SaveStatusAsync(WindowsToastStatus.Ready(AppUserModelId, exePath, shortcutPath), cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return await SaveStatusAsync(WindowsToastStatus.Failed(ex.Message), cancellationToken);
        }
    }

    public async Task<string?> LoadLastStatusTextAsync()
    {
        return await _repository.LoadSettingValueAsync(LastStatusSettingKey);
    }

    private async Task<WindowsToastStatus> SaveStatusAsync(WindowsToastStatus status, CancellationToken cancellationToken)
    {
        _status = status;
        await _repository.SaveSettingValueAsync(LastStatusSettingKey, status.ToDisplayText());
        cancellationToken.ThrowIfCancellationRequested();
        return status;
    }

    private static bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string GetShortcutPath()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        return Path.Combine(programs, "Programs", ShortcutName);
    }

    [SupportedOSPlatform("windows")]
    private static void InstallShortcut(string shortcutPath, string exePath, string appUserModelId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }

        var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
        var shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
        shellLink.SetPath(exePath);
        shellLink.SetArguments("");
        shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath));

        using (var propVariant = PropVariantHolder.FromString(appUserModelId))
        {
            var appIdKey = PropertyKeys.AppUserModelId;
            var propertyStore = (IPropertyStore)shellLink;
            propertyStore.SetValue(ref appIdKey, propVariant.Value);
            propertyStore.Commit();
        }

        ((IPersistFile)shellLink).Save(shortcutPath, true);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(IntPtr pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string? pszDir);
        void GetArguments(IntPtr pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(IntPtr pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000138-0000-0000-C000-000000000046")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    private static class PropertyKeys
    {
        public static PropertyKey AppUserModelId = new()
        {
            FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            PropertyId = 5
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr Reserved4;
    }

    private sealed class PropVariantHolder : IDisposable
    {
        public PropVariant Value;

        private PropVariantHolder(string value)
        {
            Value = new PropVariant
            {
                VariantType = 31,
                Pointer = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public static PropVariantHolder FromString(string value) => new(value);

        public void Dispose()
        {
            if (Value.Pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(Value.Pointer);
                Value.Pointer = IntPtr.Zero;
            }
        }
    }
}

public sealed record WindowsToastStatus(
    bool IsReady,
    string Message,
    string? AppUserModelId,
    string? ExecutablePath,
    string? ShortcutPath)
{
    public static WindowsToastStatus Ready(string appUserModelId, string executablePath, string shortcutPath) =>
        new(true, "Windows toast notifications are initialized.", appUserModelId, executablePath, shortcutPath);

    public static WindowsToastStatus Failed(string message) =>
        new(false, message, WindowsToastInitializationService.AppUserModelId, null, null);

    public static WindowsToastStatus NotInitialized(string message) =>
        new(false, message, WindowsToastInitializationService.AppUserModelId, null, null);

    public string ToDisplayText()
    {
        var status = IsReady ? "Ready" : "Unavailable";
        return $"{status}: {Message} AUMID={AppUserModelId} EXE={ExecutablePath} Shortcut={ShortcutPath}";
    }
}
