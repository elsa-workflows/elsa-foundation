namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// One stored row's content, as <see cref="EfSchemaChain"/> upcasts it and <see cref="IEfSchemaUpcaster.Upcast"/>
/// receives it (spec 180, FR-009; #2144): the row's table, and the decoded value of every content column the family
/// declares for that table, by property name.
/// </summary>
/// <remarks>
/// <para>
/// The table is named by <see cref="Entity"/>, the type the family's <see cref="EfSchemaContentAttribute"/> declaration
/// maps to it, since that declaration is what says which columns are content; a table every EF module maps under a name
/// of its own, such as the finalization record, is one type. The chain holds <see cref="Columns"/> to exactly that
/// declaration, before and after every step, so a row is never upcast in part and no step adds or drops a column.
/// </para>
/// <para>
/// Immutable. <see cref="Columns"/> enumerates in ordinal order of the column names, whatever order the row was built
/// in, so an upcaster that walks it is as deterministic as one that names its columns (FR-019).
/// </para>
/// </remarks>
public sealed class EfSchemaRowContent
{
    private readonly Dictionary<string, string?> _columns;

    /// <summary>A row of <paramref name="entity"/>'s table holding <paramref name="columns"/>, each named once.</summary>
    /// <exception cref="ArgumentException">A column has no name, or is named twice.</exception>
    public EfSchemaRowContent(Type entity, params ReadOnlySpan<(string Column, string? Value)> columns)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var ordered = columns.ToArray();
        foreach (var (column, _) in ordered)
            ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(columns));
        Array.Sort(ordered, (left, right) => StringComparer.Ordinal.Compare(left.Column, right.Column));

        Entity = entity;
        _columns = new Dictionary<string, string?>(ordered.Length, StringComparer.Ordinal);
        foreach (var (column, value) in ordered)
        {
            if (!_columns.TryAdd(column, value))
                throw new ArgumentException($"Column '{column}' is named more than once in a row of {entity.Name}.", nameof(columns));
        }
    }

    private EfSchemaRowContent(Type entity, Dictionary<string, string?> columns)
    {
        Entity = entity;
        _columns = columns;
    }

    /// <summary>The type the family's content declaration maps to the row's table.</summary>
    public Type Entity { get; }

    /// <summary>Every content column of the row, by property name, in ordinal order; a null value is a null column.</summary>
    public IReadOnlyDictionary<string, string?> Columns => _columns;

    /// <summary>The value of <paramref name="column"/>.</summary>
    /// <exception cref="KeyNotFoundException">The row holds no such column.</exception>
    public string? this[string column] =>
        _columns.TryGetValue(column, out var value)
            ? value
            : throw new KeyNotFoundException($"A row of {Entity.Name} holds no content column '{column}'; it holds {Names(_columns.Keys)}.");

    /// <summary>
    /// This row with <paramref name="column"/> set to <paramref name="value"/> and every other column as it is: the way an
    /// upcaster changes a column without adding one.
    /// </summary>
    /// <exception cref="ArgumentException">The row holds no such column.</exception>
    public EfSchemaRowContent With(string column, string? value)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (!_columns.ContainsKey(column))
            throw new ArgumentException($"A row of {Entity.Name} holds no content column '{column}' to set; it holds {Names(_columns.Keys)}.", nameof(column));

        // Same keys in the same order, so the copy still enumerates ordinally.
        var columns = new Dictionary<string, string?>(_columns.Count, StringComparer.Ordinal);
        foreach (var (name, current) in _columns)
            columns.Add(name, StringComparer.Ordinal.Equals(name, column) ? value : current);
        return new EfSchemaRowContent(Entity, columns);
    }

    internal static string Names(IEnumerable<string> columns) =>
        columns.Any() ? string.Join(", ", columns.Order(StringComparer.Ordinal).Select(column => $"'{column}'")) : "none";
}
