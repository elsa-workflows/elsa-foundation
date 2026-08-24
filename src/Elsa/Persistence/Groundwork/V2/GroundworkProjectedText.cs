using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Guards projected string values against the widths their storage unit declares.
/// </summary>
/// <remarks>
/// SQLite ignores declared column widths, so an over-limit value is accepted there and only fails
/// later on PostgreSQL, SQL Server or MongoDB -- writing a row the catalog cannot round-trip and
/// making the same code path succeed or fail depending on which provider is composed. Checking the
/// unit's own declaration before the write keeps all four agreeing on what is writable, and fails
/// the operation before anything is staged rather than midway through a unit of work.
/// </remarks>
public static class GroundworkProjectedText
{
    /// <summary>
    /// Throws when any projected string in <paramref name="values"/> is longer than the column it is
    /// bound to. <paramref name="lane"/> names the lane in the message so a caller can tell which
    /// catalog refused the write.
    /// </summary>
    public static void EnsureFits(StorageUnit unit, StorageValues values, string lane)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var column in unit.Columns)
        {
            if (column.MaxLength is not { } maxLength ||
                !values.Values.TryGetValue(column.Name, out var value) ||
                value is not string text ||
                text.Length <= maxLength)
                continue;
            throw new ArgumentOutOfRangeException(
                nameof(values),
                $"{lane} projected value for '{column.Name}' on unit '{unit.Id.Value}' " +
                $"exceeds its declared maximum length of {maxLength}.");
        }
    }
}
