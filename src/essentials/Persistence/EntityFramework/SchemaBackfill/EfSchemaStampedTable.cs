using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// One stamped table of a schema family, as the post-finalization backfill selects from it by stamp (spec 186, FR-005,
/// FR-023 and FR-025): in bounded batches ordered by the primary key, each resuming after the last key the one before it
/// returned, so a row the backfill cannot rewrite is passed over rather than selected again; and matching stamps as
/// opaque labels, by equality with the labels the family's declaration names, never by order.
/// </summary>
/// <remarks>
/// It selects the key and the stamp only, never the row's content, so it materializes no entity and no content column
/// is read past the chain: the rewriter reads each row through the family's own read path.
/// </remarks>
internal sealed class EfSchemaStampedTable
{
    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
    private static readonly MethodInfo PropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;
    private static readonly MethodInfo StringCompare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo PageMethod = typeof(EfSchemaStampedTable).GetMethod(nameof(PageCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo CountMethod = typeof(EfSchemaStampedTable).GetMethod(nameof(CountCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IReadOnlyList<IProperty> _key;

    private EfSchemaStampedTable(IEntityType entityType, bool contentAddressed)
    {
        Entity = entityType.ClrType;
        Name = entityType.GetSchema() is { } schema ? $"{schema}.{entityType.GetTableName()}" : entityType.GetTableName()!;
        ContentAddressed = contentAddressed;
        _key = entityType.FindPrimaryKey()?.Properties
               ?? throw new NotSupportedException($"Table '{Name}' of {Entity.Name} has no primary key, so the backfill cannot select it in batches.");
        // Every key part must be orderable in the database, or a batch could not resume after the last key it returned.
        foreach (var property in _key)
            _ = After(Expression.Parameter(Entity), property, null);
    }

    /// <summary>The type the family's context maps to the table.</summary>
    public Type Entity { get; }

    /// <summary>The table's name, with its schema when it has one: how the backfill's reports name it.</summary>
    public string Name { get; }

    /// <summary>Whether the family declares the table content-addressed, so the backfill never rewrites its rows (FR-010a).</summary>
    public bool ContentAddressed { get; }

    /// <summary>
    /// The stamped tables <paramref name="model"/> maps for <paramref name="declaration"/>'s family, as
    /// <paramref name="families"/> assigns tables to families: every stamped table of a module that owns one family, or the
    /// ones its declaration names. A table that is neither owned nor a table, such as a shared-type entity, is left out.
    /// </summary>
    public static IReadOnlyList<EfSchemaStampedTable> Of(IModel model, EfSchemaModuleFamilies families, EfSchemaFamilyDescriptor declaration)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(declaration);
        return model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null && !entityType.HasSharedClrType && EfSchemaModuleFamilies.IsStamped(entityType))
            .Where(entityType => StringComparer.Ordinal.Equals(families.FamilyOf(entityType.ClrType)?.Family, declaration.Name))
            .OrderBy(entityType => entityType.GetTableName(), StringComparer.Ordinal)
            .Select(entityType => new EfSchemaStampedTable(
                entityType,
                declaration.ContentAddressed.Any(type => type.IsAssignableFrom(entityType.ClrType))))
            .ToArray();
    }

    /// <summary>
    /// At most <paramref name="take"/> rows <paramref name="stamps"/> selects, ordered by key and after
    /// <paramref name="after"/> when given: each row's key, in the model's order, and its stamp.
    /// </summary>
    public Task<IReadOnlyList<EfSchemaStampedRow>> PageAsync(DbContext context, EfSchemaStampFilter stamps, object?[]? after, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stamps);
        if (stamps.IsEmpty)
            return Task.FromResult<IReadOnlyList<EfSchemaStampedRow>>([]);
        return (Task<IReadOnlyList<EfSchemaStampedRow>>)PageMethod.MakeGenericMethod(Entity).Invoke(null, [this, context, stamps, after, take, cancellationToken])!;
    }

    /// <summary>How many rows <paramref name="stamps"/> selects.</summary>
    public Task<long> CountAsync(DbContext context, EfSchemaStampFilter stamps, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stamps);
        if (stamps.IsEmpty)
            return Task.FromResult(0L);
        return (Task<long>)CountMethod.MakeGenericMethod(Entity).Invoke(null, [this, context, stamps, cancellationToken])!;
    }

    /// <summary>A row's key as a report names it: its values, in the model's order.</summary>
    public string Describe(IReadOnlyList<object?> key) =>
        string.Join(", ", _key.Select((property, index) => $"{property.Name}={key[index]}"));

    private static async Task<IReadOnlyList<EfSchemaStampedRow>> PageCoreAsync<TEntity>(
        EfSchemaStampedTable table,
        DbContext context,
        EfSchemaStampFilter stamps,
        object?[]? after,
        int take,
        CancellationToken cancellationToken) where TEntity : class
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        var predicate = stamps.Predicate(Property(row, typeof(string), EfSchemaVersion.ColumnName));
        if (after is not null)
            predicate = Expression.AndAlso(predicate, table.After(row, after));

        IQueryable<TEntity> query = context.Set<TEntity>().AsNoTracking().Where(Expression.Lambda<Func<TEntity, bool>>(predicate, row));
        for (var index = 0; index < table._key.Count; index++)
        {
            var key = table._key[index];
            var order = Expression.Lambda(Property(row, key.ClrType, key.Name), row);
            query = (IQueryable<TEntity>)query.Provider.CreateQuery(Expression.Call(
                typeof(Queryable),
                index == 0 ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy),
                [typeof(TEntity), key.ClrType],
                query.Expression,
                Expression.Quote(order)));
        }

        var selected = Expression.Lambda<Func<TEntity, object?[]>>(
            Expression.NewArrayInit(
                typeof(object),
                table._key.Select(key => Expression.Convert(Property(row, key.ClrType, key.Name), typeof(object)))
                    .Append<Expression>(Property(row, typeof(string), EfSchemaVersion.ColumnName))),
            row);
        var rows = await query.Select(selected).Take(take).ToListAsync(cancellationToken);
        return rows.Select(values => new EfSchemaStampedRow(values[..^1], (string?)values[^1])).ToArray();
    }

    private static async Task<long> CountCoreAsync<TEntity>(
        EfSchemaStampedTable table,
        DbContext context,
        EfSchemaStampFilter stamps,
        CancellationToken cancellationToken) where TEntity : class
    {
        _ = table;
        var row = Expression.Parameter(typeof(TEntity), "row");
        var predicate = stamps.Predicate(Property(row, typeof(string), EfSchemaVersion.ColumnName));
        return await context.Set<TEntity>().AsNoTracking().LongCountAsync(Expression.Lambda<Func<TEntity, bool>>(predicate, row), cancellationToken);
    }

    /// <summary>
    /// "The row's key comes after <paramref name="after"/>", lexicographically over the key's parts:
    /// <c>(k1 &gt; v1) OR (k1 = v1 AND k2 &gt; v2) OR ...</c>, each value a query parameter rather than a literal, so every
    /// batch of a table shares one cached query.
    /// </summary>
    private Expression After(ParameterExpression row, object?[] after)
    {
        Expression? any = null;
        Expression? equalSoFar = null;
        for (var index = 0; index < _key.Count; index++)
        {
            var key = _key[index];
            var greater = After(row, key, after[index]);
            var step = equalSoFar is null ? greater : Expression.AndAlso(equalSoFar, greater);
            any = any is null ? step : Expression.OrElse(any, step);
            var equal = Expression.Equal(Property(row, key.ClrType, key.Name), Parameter(key.ClrType, after[index]));
            equalSoFar = equalSoFar is null ? equal : Expression.AndAlso(equalSoFar, equal);
        }

        return any!;
    }

    /// <summary>
    /// "This key part comes after <paramref name="value"/>": <c>string.Compare</c> for text, which EF translates to
    /// <c>&gt;</c>; an enum stored as its number compared as that number, as C# compares enums; and the type's own operator
    /// otherwise. A key part stored through a value converter of any other type is refused, since its order in the
    /// database need not be its order here.
    /// </summary>
    private Expression After(ParameterExpression row, IProperty key, object? value)
    {
        var column = Property(row, key.ClrType, key.Name);
        var bound = Parameter(key.ClrType, value);
        if (key.ClrType == typeof(string))
            return Expression.GreaterThan(Expression.Call(StringCompare, column, bound), Expression.Constant(0));
        var enumType = Nullable.GetUnderlyingType(key.ClrType) ?? key.ClrType;
        if (enumType.IsEnum && key.GetValueConverter() is null)
        {
            var number = Enum.GetUnderlyingType(enumType);
            return Expression.GreaterThan(Expression.Convert(column, number), Expression.Convert(bound, number));
        }

        try
        {
            if (key.GetValueConverter() is not null)
                throw new InvalidOperationException("The key part is stored through a value converter.");
            return Expression.GreaterThan(column, bound);
        }
        catch (InvalidOperationException exception)
        {
            throw new NotSupportedException(
                $"Table '{Name}' of {Entity.Name} has a key part '{key.Name}' of type {key.ClrType.Name}, which has no order the backfill can resume a batch after.",
                exception);
        }
    }

    private static Expression Property(ParameterExpression row, Type type, string name) =>
        Expression.Call(PropertyMethod.MakeGenericMethod(type), row, Expression.Constant(name));

    /// <summary>A value EF sends as a parameter: a field of a captured box, as a closure variable is.</summary>
    private static Expression Parameter(Type type, object? value)
    {
        var box = Activator.CreateInstance(typeof(StrongBox<>).MakeGenericType(type), value ?? (type.IsValueType ? Activator.CreateInstance(type) : null))!;
        return Expression.Field(Expression.Constant(box), nameof(StrongBox<object>.Value));
    }
}

/// <summary>One row a batch selected: its key, in the model's order, and the stamp it carried then.</summary>
internal sealed record EfSchemaStampedRow(object?[] Key, string? Stamp);

/// <summary>
/// Which stamps a selection matches, as opaque labels (spec 180, FR-004): exactly <see cref="Versions"/>, or anything
/// else, a missing stamp included.
/// </summary>
internal sealed record EfSchemaStampFilter(bool Excluding, IReadOnlyList<string> Versions)
{
    /// <summary>Rows stamped with one of <paramref name="versions"/>.</summary>
    public static EfSchemaStampFilter In(IEnumerable<string> versions) => new(false, versions.ToArray());

    /// <summary>Rows stamped with none of <paramref name="versions"/>, or with no stamp at all.</summary>
    public static EfSchemaStampFilter NotIn(IEnumerable<string> versions) => new(true, versions.ToArray());

    /// <summary>Whether it can select nothing, so no query needs to run.</summary>
    public bool IsEmpty => !Excluding && Versions.Count == 0;

    /// <summary>The predicate over <paramref name="stamp"/>, each label a literal: a family has only a few.</summary>
    public Expression Predicate(Expression stamp)
    {
        var labels = Versions.Select(version => Expression.Equal(stamp, Expression.Constant(version, typeof(string)))).ToArray();
        if (!Excluding)
            return labels.Aggregate<Expression>(Expression.OrElse);
        var missing = Expression.Equal(stamp, Expression.Constant(null, typeof(string)));
        return labels.Length == 0
            ? Expression.Constant(true)
            : Expression.OrElse(missing, Expression.Not(labels.Aggregate<Expression>(Expression.OrElse)));
    }
}
