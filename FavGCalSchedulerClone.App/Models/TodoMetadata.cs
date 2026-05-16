namespace FavGCalSchedulerClone.App.Models;

public sealed record TodoMetadata(string Priority, int Progress)
{
    public bool IsDone => Progress >= 100;
    public string ProgressText => $"{Progress}%";
}
