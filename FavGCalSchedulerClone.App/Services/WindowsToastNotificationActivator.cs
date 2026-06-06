using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FavGCalSchedulerClone.App.Services;

[ComVisible(true)]
[Guid("D8DE4697-BC42-4D95-B9D5-0AF5F8E5B07A")]
public sealed class WindowsToastNotificationActivator : NotificationActivator
{
    public override void OnActivated(string arguments, NotificationUserInput userInput, string appUserModelId)
    {
        WindowsToastActivationBridge.RaiseActivated(arguments);
    }
}

public static class WindowsToastActivationBridge
{
    public static event EventHandler<string>? Activated;

    public static void RaiseActivated(string arguments)
    {
        Activated?.Invoke(null, arguments);
    }
}
