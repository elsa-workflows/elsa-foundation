using System.Data.Common;
using System.Text.RegularExpressions;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Reads the command the store sends to create a finalization row (<c>EfInsertIfAbsent</c>, #2162), so a test pins a moment
/// in a seed by the command rather than by a save: which table it inserts into, matched by name and never by substring,
/// and the value it carries for a column, which the command names as its parameter.
/// </summary>
internal static partial class FinalizationInsert
{
    /// <summary>The table <paramref name="command"/> inserts into, or null when it is no insert.</summary>
    public static string? TableOf(DbCommand command) => InsertInto().Match(command.CommandText) is { Success: true } match ? match.Groups["table"].Value : null;

    /// <summary>Whether <paramref name="command"/> inserts into the table named exactly <paramref name="table"/>.</summary>
    public static bool Into(DbCommand command, string table) => TableOf(command) == table;

    /// <summary>Whether <paramref name="command"/> inserts into a record table of some module.</summary>
    public static bool IntoAnyRecordTable(DbCommand command) => TableOf(command)?.StartsWith(EfSchemaFinalization.RecordTablePrefix, StringComparison.Ordinal) == true;

    /// <summary>Whether <paramref name="command"/> inserts into a record table or a database identity table of some module.</summary>
    public static bool IntoAnyFinalizationTable(DbCommand command) =>
        IntoAnyRecordTable(command) || TableOf(command)?.StartsWith(EfSchemaFinalization.DatabaseIdentityTablePrefix, StringComparison.Ordinal) == true;

    /// <summary>The value the insert carries for <paramref name="column"/>, found by the parameter's name.</summary>
    public static object? Value(DbCommand command, string column) =>
        command.Parameters.Cast<DbParameter>().Single(parameter => parameter.ParameterName.TrimStart('@', ':', '$') == column).Value;

    // INSERT INTO [schema].[table] ( ...: the identifier is quoted with double quotes, brackets or backticks, and may be schema-qualified.
    [GeneratedRegex(@"^\s*INSERT\s+INTO\s+(?:[^\s(]+\.)?[""`\[](?<table>[^""`\]]+)[""`\]]\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InsertInto();
}
