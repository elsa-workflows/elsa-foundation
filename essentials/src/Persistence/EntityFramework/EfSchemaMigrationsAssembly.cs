using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Puts a module's migrations in the configured schema without those migrations naming one.
/// </summary>
/// <remarks>
/// <para>
/// Migrations are scaffolded against a context with no default schema, so every operation they build leaves
/// <c>Schema</c> null and the DDL comes out unqualified — a host with a schema configured would then query
/// <c>elsa.table</c> while its migrations created <c>dbo.table</c>. <c>HasDefaultSchema</c> does not reach
/// migration DDL, because the SQL generator reads the schema off each operation, not off the model.
/// </para>
/// <para>
/// Filling the operations in is what makes one scaffolded migration set apply into any schema, which is the point:
/// the alternative is a schema baked into every generated migration file, where the host can no longer choose it.
/// </para>
/// </remarks>
[SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "MigrationsAssembly is the only seam between reading a migration and generating its SQL.")]
public sealed class EfSchemaMigrationsAssembly : MigrationsAssembly
{
    private static readonly ConcurrentDictionary<Type, Shape> Shapes = new();
    private readonly string? schema;

    public EfSchemaMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger) =>
        schema = EfSchemaOptionsExtension.Find(options);

    public override Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        var migration = base.CreateMigration(migrationClass, activeProvider);
        if (schema is null)
            return migration;

        Qualify(migration.UpOperations, schema, migrationClass.Name);
        Qualify(migration.DownOperations, schema, migrationClass.Name);
        return migration;
    }

    /// <summary>
    /// Fills in every schema a set of scaffolded operations left null, nested operations included. Public so the
    /// rewrite can be proven against an operation tree on its own, without a database or a provider engine.
    /// </summary>
    public static void Qualify(IEnumerable<MigrationOperation> operations, string schema, string migrationName)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        foreach (var operation in operations)
        {
            // Raw SQL is opaque: whatever it names stays in whatever schema it named. Silently applying it to the
            // wrong schema is the failure this whole class exists to prevent, so refuse instead.
            if (operation is SqlOperation)
            {
                throw new NotSupportedException(
                    $"{migrationName} runs raw SQL, which cannot be redirected to schema '{schema}'. " +
                    "Schema-qualify the statement in the migration, or run this module without a configured schema.");
            }

            var shape = Shapes.GetOrAdd(operation.GetType(), Describe);
            foreach (var property in shape.Schemas)
            {
                if (property.GetValue(operation) is null)
                    property.SetValue(operation, schema);
            }

            foreach (var property in shape.Nested)
            {
                switch (property.GetValue(operation))
                {
                    case MigrationOperation nested:
                        Qualify([nested], schema, migrationName);
                        break;
                    case IEnumerable<MigrationOperation> nested:
                        Qualify(nested, schema, migrationName);
                        break;
                }
            }
        }
    }

    private static Shape Describe(Type operation)
    {
        var properties = operation.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return new Shape(
            // PrincipalSchema is a foreign key's target and NewSchema a rename's destination; both are this schema,
            // because every table a first-party module migration touches lives in it.
            [.. properties.Where(property =>
                property is { CanRead: true, CanWrite: true } &&
                property.PropertyType == typeof(string) &&
                property.Name is "Schema" or "PrincipalSchema" or "NewSchema")],
            // CreateTable carries its columns, keys and constraints as operations of their own, and an alter
            // carries the old shape it is diffed against; each of those names a schema too.
            [.. properties.Where(property =>
                property.CanRead &&
                (typeof(MigrationOperation).IsAssignableFrom(property.PropertyType) ||
                 typeof(IEnumerable<MigrationOperation>).IsAssignableFrom(property.PropertyType)))]);
    }

    private sealed record Shape(PropertyInfo[] Schemas, PropertyInfo[] Nested);
}
