namespace FavGCalSchedulerClone.App.Services;

internal static class CsvCellSanitizer
{
    public static string NeutralizeForSpreadsheet(string? value)
    {
        value ??= string.Empty;
        return value.Length > 0 && IsFormulaPrefix(value[0])
            ? "'" + value
            : value;
    }

    public static string RestoreNeutralizedValue(string? value)
    {
        value ??= string.Empty;
        return value.Length > 1 && value[0] == '\'' && IsFormulaPrefix(value[1])
            ? value[1..]
            : value;
    }

    internal static bool IsFormulaPrefix(char value)
        => value is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n';
}
