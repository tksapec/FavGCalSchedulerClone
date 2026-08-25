using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

internal static class CalendarTagAtomicWriter
{
    public static async Task SaveTagsAsync(
        CalendarRepository repository,
        IEnumerable<CalendarTag> tags,
        CancellationToken cancellationToken = default)
    {
        var items = tags.ToArray();
        if (items.Length == 0)
        {
            return;
        }

        await repository.InitializeAsync();
        await using var connection = repository.OpenConnection();
        await using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var tag in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT OR REPLACE INTO tags(name, color, is_visible, priority)
                    VALUES($name, $color, $visible, $priority)
                    """;
                command.Parameters.AddWithValue("$name", tag.Name);
                command.Parameters.AddWithValue("$color", tag.Color);
                command.Parameters.AddWithValue("$visible", tag.IsVisible ? 1 : 0);
                command.Parameters.AddWithValue("$priority", tag.Priority);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
