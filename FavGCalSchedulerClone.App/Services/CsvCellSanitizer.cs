namespace FavGCalSchedulerClone.App.Services;

internal static class CsvCellSanitizer
{
    public static string NeutralizeForSpreadsheet(string? value)
    {
        value ??= "";
        if (value.Length > 0 && IsFormulaPrefix(value[0]))
        {
            return "'" + value;
        }

        // A literal apostrophe immediately before a formula prefix is otherwise
        // indistinguishable from our spreadsheet-neutralization marker on import.
        return value.Length > 1 && value[0] == '\'' && IsFormulaPrefix(value[1])
            ? "'" + value
            : value;
    }

    public static string RestoreNeutralizedValue(string? value)
    {
        value ??= "";
        if (value.Length > 2 && value[0] == '\'' && value[1] == '\'' && IsFormulaPrefix(value[2]))
        {
            return value[1..];
        }

        return value.Length > 1 && value[0] == '\'' && IsFormulaPrefix(value[1])
            ? value[1..]
            : value;
    }

    private static bool IsFormulaPrefix(char value) =>
        value is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
            or '＝' or '＋' or '－' or '＠';
}
