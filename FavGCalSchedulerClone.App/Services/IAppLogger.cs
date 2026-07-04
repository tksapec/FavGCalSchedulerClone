namespace FavGCalSchedulerClone.App.Services;

public interface IAppLogger
{
    void LogError(Exception exception, string context);

    void LogInfo(string message);
}
