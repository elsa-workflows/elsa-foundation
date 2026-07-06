using Elsa.Persistence.EFCore.Contracts;
using Elsa.Primitives.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Text;

namespace Elsa.Persistence.EFCore.Services;

public sealed class UpsertCommandGenerator : IUpsertCommandGenerator
{
    public GeneratedCommand Generate<TDbContext, TEntity>(TDbContext dbContext, IList<TEntity> entities, Expression<Func<TEntity, string>> keySelector)
        where TDbContext : DbContext
        where TEntity : Entity
    {
        // Identify the current provider (e.g., "Microsoft.EntityFrameworkCore.SqlServer")
        var providerName = dbContext.Database.ProviderName?.ToLowerInvariant() ?? string.Empty;

        // Determine the method for generating SQL based on the provider
        Func<DbContext, IList<TEntity>, Expression<Func<TEntity, string>>, GeneratedCommand> generateSql = providerName switch
        {
            var pn when pn.Contains("sqlserver") => GenerateSqlServerUpsert,
            var pn when pn.Contains("sqlite") => GenerateSqliteUpsert,
            var pn when pn.Contains("postgres") => GeneratePostgresUpsert,
            var pn when pn.Contains("mysql") => GenerateMySqlUpsert,
            var pn when pn.Contains("oracle") => GenerateOracleUpsert,

            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported.")
        };

        return generateSql(dbContext, entities, keySelector);
    }

    /// <summary>
    /// Provider-invariant table/column shape: the EF entity type, the store object the columns resolve
    /// against, the ordered property/column lists, and the key column. Every dialect reads exactly this
    /// before emitting its syntax, so it is resolved once here rather than re-derived in each Generate*
    /// method (the source of the pre-refactor duplication, #415 item 2).
    /// </summary>
    private readonly record struct EntityShape(
        IEntityType EntityType,
        StoreObjectIdentifier StoreObject,
        IReadOnlyList<IProperty> Properties,
        string KeyColumnName,
        IReadOnlyList<string> ColumnNames);

    private static EntityShape ResolveEntityShape<TEntity>(DbContext dbContext, Expression<Func<TEntity, string>> keySelector)
        where TEntity : class
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity))!;
        var storeObject = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var props = entityType.GetProperties().ToList();
        var keyProp = entityType.FindProperty(keySelector.GetMemberAccess().Name)!;
        var keyColumnName = keyProp.GetColumnName(storeObject)!;
        var columnNames = props.Select(p => p.GetColumnName(storeObject)!).ToList();

        return new(entityType, storeObject, props, keyColumnName, columnNames);
    }

    /// <summary>
    /// Reads one entity's column values in <paramref name="properties"/> order — honoring shadow properties
    /// and value converters exactly as EF would — appending each provider value to <paramref name="parameters"/>
    /// and returning the placeholder fragment for each column. <paramref name="placeholderFactory"/> lets a
    /// dialect wrap the <c>{n}</c> token (e.g. Postgres <c>CAST(... AS jsonb)</c>, SqlServer varbinary-NULL
    /// cast, Oracle <c>AS column</c> alias) or substitute a literal; it receives the property, its provider
    /// value, and the token. The value is always appended to <paramref name="parameters"/> so the parameter
    /// sequence is identical across dialects even when a placeholder emits a literal — matching the
    /// pre-refactor behavior byte-for-byte.
    /// </summary>
    private static List<string> ExtractRowValues(
        DbContext dbContext,
        object entity,
        IReadOnlyList<IProperty> properties,
        List<object> parameters,
        ref int parameterCount,
        Func<IProperty, object?, string, string> placeholderFactory)
    {
        var placeholders = new List<string>(properties.Count);

        foreach (var property in properties)
        {
            var token = $"{{{parameterCount++}}}";

            // If it's a shadow property, retrieve value via Entry(..).Property(..)
            var value = property.IsShadowProperty()
                ? dbContext.Entry(entity).Property(property.Name).CurrentValue
                : property.PropertyInfo?.GetValue(entity);

            var converter = property.GetTypeMapping().Converter;
            if (converter != null)
                value = converter.ConvertToProvider(value);

            placeholders.Add(placeholderFactory(property, value, token));
            parameters.Add(value!);
        }

        return placeholders;
    }

    private static GeneratedCommand GenerateSqlServerUpsert<TEntity>(
        DbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector)
        where TEntity : class
    {
        var shape = ResolveEntityShape(dbContext, keySelector);
        var tableName = $"[{shape.EntityType.GetSchema()}].[{shape.EntityType.GetTableName()}]";
        var keyColumnName = $"[{shape.KeyColumnName}]";
        var columnNames = shape.ColumnNames.Select(c => $"[{c}]").ToList();

        var mergeSql = new StringBuilder();
        mergeSql.AppendLine($"MERGE {tableName} AS Target");
        mergeSql.AppendLine("USING (VALUES");

        var parameters = new List<object>();
        var parameterCount = 0;

        for (var i = 0; i < entities.Count; i++)
        {
            var values = ExtractRowValues(
                dbContext,
                entities[i],
                shape.Properties,
                parameters,
                ref parameterCount,
                // Explicitly cast null values for varbinary columns.
                static (property, value, token) =>
                    property.GetColumnType().StartsWith("varbinary", StringComparison.OrdinalIgnoreCase) && value is null
                        ? "CAST(NULL AS varbinary(max))"
                        : token);

            var line = $"({string.Join(", ", values)}){(i < entities.Count - 1 ? "," : string.Empty)}";
            mergeSql.AppendLine(line);
        }

        mergeSql.AppendLine($") AS Source ({string.Join(", ", columnNames)})");
        mergeSql.AppendLine($"ON Target.{keyColumnName} = Source.{keyColumnName}");
        mergeSql.AppendLine("WHEN MATCHED THEN");
        mergeSql.AppendLine($"    UPDATE SET {string.Join(", ", columnNames.Where(c => c != keyColumnName).Select(c => $"Target.{c} = Source.{c}"))}");
        mergeSql.AppendLine("WHEN NOT MATCHED THEN");
        mergeSql.AppendLine($"    INSERT ({string.Join(", ", columnNames)})");
        mergeSql.AppendLine($"    VALUES ({string.Join(", ", columnNames.Select(c => $"Source.{c}"))});");

        return new(mergeSql.ToString(), [.. parameters]);
    }

    private static GeneratedCommand GenerateSqliteUpsert<TEntity>(
        DbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector)
        where TEntity : class
    {
        var shape = ResolveEntityShape(dbContext, keySelector);
        var tableName = shape.EntityType.GetTableName();
        var keyColumnName = shape.KeyColumnName;
        var columnNames = shape.ColumnNames;

        var sb = new StringBuilder();
        var parameters = new List<object>();
        var parameterCount = 0;

        sb.Append($"INSERT INTO \"{tableName}\" ({string.Join(", ", columnNames.Select(c => $"\"{c}\""))}) VALUES ");

        for (var i = 0; i < entities.Count; i++)
        {
            var placeholders = ExtractRowValues(dbContext, entities[i], shape.Properties, parameters, ref parameterCount, PlainPlaceholder);

            sb.Append($"({string.Join(", ", placeholders)})");
            if (i < entities.Count - 1)
                sb.Append(", ");
        }

        sb.AppendLine();
        sb.AppendLine($"ON CONFLICT(\"{keyColumnName}\") DO UPDATE SET");

        var updateAssignments = columnNames
            .Where(c => c != keyColumnName)
            .Select(c => $"\"{c}\"=excluded.\"{c}\"");

        sb.AppendLine(string.Join(", ", updateAssignments) + ";");

        return new(sb.ToString(), [.. parameters]);
    }

    private static GeneratedCommand GeneratePostgresUpsert<TEntity>(
        DbContext dbContext,
        IList<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector)
        where TEntity : class
    {
        var shape = ResolveEntityShape(dbContext, keySelector);
        var storeObject = shape.StoreObject;
        var keyColumnName = shape.KeyColumnName;
        var columnNames = shape.ColumnNames;

        var sb = new StringBuilder();
        var parameters = new List<object>();
        var parameterCount = 0;

        sb.Append($"INSERT INTO \"{storeObject.Schema}\".\"{storeObject.Name}\" ({string.Join(", ", columnNames.Select(c => $"\"{c}\""))}) VALUES ");

        for (var i = 0; i < entities.Count; i++)
        {
            var placeholders = ExtractRowValues(
                dbContext,
                entities[i],
                shape.Properties,
                parameters,
                ref parameterCount,
                // Detect json/jsonb column types and cast the parameter so PostgreSQL accepts it.
                static (property, _, token) =>
                {
                    var columnType = property.GetColumnType();
                    if (columnType.StartsWith("jsonb", StringComparison.OrdinalIgnoreCase))
                        return $"CAST({token} AS jsonb)";
                    if (columnType.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                        return $"CAST({token} AS json)";
                    return token;
                });

            sb.Append($"({string.Join(", ", placeholders)})");
            if (i < entities.Count - 1)
                sb.Append(", ");
        }

        sb.AppendLine();
        sb.AppendLine($"ON CONFLICT (\"{keyColumnName}\") DO UPDATE SET");

        var updateAssignments = columnNames
            .Where(c => c != keyColumnName)
            .Select(c => $"\"{c}\" = EXCLUDED.\"{c}\"");

        sb.AppendLine(string.Join(", ", updateAssignments) + ";");

        return new(sb.ToString(), [.. parameters]);
    }

    private static GeneratedCommand GenerateMySqlUpsert<TEntity>(DbContext dbContext, IList<TEntity> entities, Expression<Func<TEntity, string>> keySelector)
        where TEntity : class
    {
        var shape = ResolveEntityShape(dbContext, keySelector);
        var tableName = shape.EntityType.GetTableName();
        var keyColumnName = shape.KeyColumnName;
        var columnNames = shape.ColumnNames;

        var sb = new StringBuilder();
        var parameters = new List<object>();
        var parameterCount = 0;

        sb.Append($"INSERT INTO `{tableName}` ({string.Join(", ", columnNames.Select(c => $"`{c}`"))}) VALUES ");

        for (var i = 0; i < entities.Count; i++)
        {
            var placeholders = ExtractRowValues(dbContext, entities[i], shape.Properties, parameters, ref parameterCount, PlainPlaceholder);

            sb.Append($"({string.Join(", ", placeholders)})");
            if (i < entities.Count - 1)
                sb.Append(", ");
        }

        sb.AppendLine();
        sb.AppendLine("ON DUPLICATE KEY UPDATE");

        var updateAssignments = columnNames
            .Where(c => c != keyColumnName)
            .Select(c => $"`{c}` = VALUES(`{c}`)");

        sb.AppendLine(string.Join(", ", updateAssignments) + ";");

        return new(sb.ToString(), [.. parameters]);
    }

    private static GeneratedCommand GenerateOracleUpsert<TEntity>(DbContext dbContext, IList<TEntity> entities, Expression<Func<TEntity, string>> keySelector)
        where TEntity : class
    {
        var shape = ResolveEntityShape(dbContext, keySelector);
        var schema = shape.EntityType.GetSchema();
        var tableName = shape.EntityType.GetTableName();
        var fullName = !string.IsNullOrEmpty(schema) ? $"{schema}.{tableName}" : tableName;
        var keyColumnName = shape.KeyColumnName;
        var columnNames = shape.ColumnNames;

        var sb = new StringBuilder();
        var parameters = new List<object>();
        var parameterCount = 0;

        sb.AppendLine($"MERGE INTO {fullName} Target");
        sb.AppendLine("USING (SELECT");

        for (var i = 0; i < entities.Count; i++)
        {
            var columnIndex = 0;
            var lineParts = ExtractRowValues(
                dbContext,
                entities[i],
                shape.Properties,
                parameters,
                ref parameterCount,
                // Oracle aliases must match the column name.
                (_, _, token) => $"{token} AS {columnNames[columnIndex++]}");

            // Comma if not last
            var suffix = (i < entities.Count - 1) ? " FROM DUAL UNION ALL SELECT" : " FROM DUAL";
            sb.AppendLine(string.Join(", ", lineParts) + suffix);
        }

        sb.AppendLine($") Source ON (Target.{keyColumnName} = Source.{keyColumnName})");
        sb.AppendLine("WHEN MATCHED THEN UPDATE SET");

        var updateSetClauses = columnNames
            .Where(c => c != keyColumnName)
            .Select(c => $"Target.{c} = Source.{c}");

        sb.AppendLine(string.Join(", ", updateSetClauses));
        sb.AppendLine("WHEN NOT MATCHED THEN");
        sb.AppendLine($"INSERT ({string.Join(", ", columnNames)})");
        sb.AppendLine($"VALUES ({string.Join(", ", columnNames.Select(c => $"Source.{c}"))});");

        return new(sb.ToString(), [.. parameters]);
    }

    private static string PlainPlaceholder(IProperty property, object? value, string token) => token;
}
