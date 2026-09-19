using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Every assertion here reads the <em>generated migration</em>, never the model.
/// <para>
/// That distinction is the whole point of elsa-workflows/elsa-foundation#1837. Activities Design declared
/// <c>Latin1_General_100_BIN2</c> and <c>C</c> at model level, a test of the model's configured collation
/// would have passed, and the shipped SQL Server and PostgreSQL migrations contained the word "collation"
/// zero times: every one of those columns inherited the server's default, which is linguistic on a stock
/// SQL Server and on a stock PostgreSQL. A model-level declaration never reached the schema.
/// </para>
/// </summary>
public sealed class OrdinalCollationMigrationTests
{
    /// <summary>The modules #1837 put in scope, by the prefix their context names share.</summary>
    private static readonly string[] CollatingModules =
    [
        "ActivitiesDesign", "Elsa3Import", "IdentityIam", "IdentityProviderConfiguration",
        "PublishingSnapshotReview", "Secrets", "WorkflowsDesign"
    ];

    /// <summary>
    /// The columns the correctness argument rests on, per module. Delete one from a module's ordinal list
    /// and this is what goes red — on every provider, out of the migration rather than out of the model.
    /// </summary>
    private static readonly (string Module, string Table, string Column)[] MustBeOrdinal =
    [
        // The keyset cursor EfActivityDesignStores pages the management projection with.
        ("ActivitiesDesign", "elsa_activity_management_definitions", "SortKey"),
        ("ActivitiesDesign", "elsa_activity_management_definitions", "ResourceId"),
        ("ActivitiesDesign", "elsa_activity_management_definitions", "TenantScopeKey"),
        ("ActivitiesDesign", "elsa_activity_management_definitions", "ResourceIdIdentityHash"),
        ("ActivitiesDesign", "elsa_activity_management_definitions", "IdIdentityHash"),
        // ByReference matches a reference's raw value next to its hash; both sides have to be ordinal.
        ("ActivitiesDesign", "elsa_activity_definition_versions_v2", "DefinitionId"),
        ("ActivitiesDesign", "elsa_activity_definition_versions_v2", "DefinitionIdIdentityHash"),
        ("ActivitiesDesign", "elsa_activity_definition_versions_v2", "SemVerSortKey"),
        ("Elsa3Import", "elsa3_reusable_import_collections", "TenantKey"),
        ("Elsa3Import", "elsa3_reusable_import_collections", "HandleHash"),
        ("Elsa3Import", "elsa3_reusable_import_collections", "ContentHash"),
        // A case-insensitive default here lets two reservations differing only in case collide.
        ("IdentityIam", "identity_email_reservations", "NormalizedEmailKey"),
        ("IdentityIam", "identity_user_name_reservations", "NormalizedUserNameKey"),
        ("IdentityIam", "identity_users", "TenantLookupKey"),
        ("IdentityProviderConfiguration", "identity_provider_configurations", "ProviderLookupKey"),
        ("IdentityProviderConfiguration", "identity_global_provider_configurations", "TenantLookupKey"),
        ("PublishingSnapshotReview", "elsa_publication_records", "PublicationIdHash"),
        ("PublishingSnapshotReview", "elsa_publication_snapshot_reviews", "PreflightToken"),
        ("Secrets", "elsa_secrets", "NormalizedName"),
        ("Secrets", "elsa_secrets", "ScopeLookupKey"),
        ("WorkflowsDesign", "elsa_workflow_definitions_v2", "IdLookupHash"),
        ("WorkflowsDesign", "elsa_workflow_definition_versions", "SemVerSortKey")
    ];

    /// <summary>
    /// Content columns, which must not be collated. Giving one a binary collation is a silent behaviour
    /// change for anything that searches it, so the scope is "compared or ordered", not "is a string".
    /// </summary>
    private static readonly (string Module, string Table, string Column)[] MustNotBeOrdinal =
    [
        ("ActivitiesDesign", "elsa_activity_availability_settings", "Rules"),
        ("ActivitiesDesign", "elsa_activity_management_definitions", "SearchText"),
        ("ActivitiesDesign", "elsa_activity_definitions", "Description"),
        ("ActivitiesDesign", "elsa_activity_definitions", "DisplayName"),
        ("Elsa3Import", "elsa3_reusable_import_collections", "ContentJson"),
        ("IdentityIam", "identity_users", "PasswordHash"),
        ("IdentityIam", "identity_roles", "PermissionsJson"),
        ("IdentityProviderConfiguration", "identity_provider_configurations", "SettingsJson"),
        ("PublishingSnapshotReview", "elsa_publication_records", "FailureMessage"),
        ("PublishingSnapshotReview", "elsa_activity_draft_test_runs", "Content"),
        ("Secrets", "elsa_secrets", "Payload"),
        ("WorkflowsDesign", "elsa_design_operations", "ResultJson"),
        ("WorkflowsDesign", "elsa_workflow_definition_drafts", "StateSource")
    ];

    public static TheoryData<string> Providers() => [.. ModuleContextCatalog.Providers];

    /// <summary>The providers that declare a collation at all. SQLite's default already is <c>BINARY</c>.</summary>
    public static TheoryData<string> CollatingProviders() =>
        [.. ModuleContextCatalog.Providers.Where(provider => provider != "Sqlite")];

    /// <summary>
    /// The claim the fix is: whatever a module declares per column reaches the migration. Nothing here
    /// consults the model for what <em>should</em> be collated; it compares the two, and a model-level
    /// declaration lands in the model and in no migration, which is exactly what it caught in #1837.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void Each_module_migration_carries_the_collation_its_model_declares_per_column(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            Assert.Equal(DeclaredCollations(context), MigratedCollations(context, provider));
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_collated_column_uses_the_one_binary_collation_declared_for_the_provider(string provider)
    {
        var expected = EfOrdinalCollation.ForProvider(EfRelationalProviderBinding.ExpectedProviderName(provider));
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            var used = MigratedCollations(context, provider).Values.Distinct(StringComparer.Ordinal).ToArray();
            // SQLite's BINARY is its default, so the whole family declares nothing there.
            Assert.Equal(expected is null || !InScope(type) ? [] : new[] { expected }, used);
        }
    }

    /// <summary>
    /// The placement half of the decision. A model-level collation is a database-wide one, and modules can
    /// share a database, so one module would be setting its neighbours' comparison semantics — and on two
    /// providers it silently reached nothing at all. Folded across the chain, because Secrets keeps its
    /// historical migrations: its MySQL <c>Initial</c> did declare a table collation, and the migration
    /// added here is what takes it away again.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void No_module_leaves_a_collation_on_the_database_or_a_whole_table(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            // Collation lives in the design-time model; the read-optimized one throws rather than answering.
            Assert.Null(context.GetService<IDesignTimeModel>().Model.GetCollation());

            var owners = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var operation in Operations(context, provider))
            {
                var owner = operation switch
                {
                    CreateTableOperation table => table.Name,
                    AlterTableOperation table => table.Name,
                    AlterDatabaseOperation => "the database",
                    _ => null
                };
                if (owner is null)
                    continue;
                var declared = (operation as AlterDatabaseOperation)?.Collation
                               ?? operation.FindAnnotation(RelationalAnnotationNames.Collation)?.Value as string;
                if (declared is null)
                    owners.Remove(owner);
                else
                    owners[owner] = declared;
            }

            Assert.True(
                owners.Count == 0,
                $"{type.Name} leaves a collation on {string.Join(", ", owners.Select(entry => $"{entry.Key} ({entry.Value})"))}. #1837: per column, never at model level.");
        }
    }

    /// <summary>
    /// MySQL's second channel, and the one that fails quietly. Oracle's provider reads
    /// <c>MySQL:Collation</c> out of the migration's target model — the <c>.Designer.cs</c> beside it — and
    /// ignores the relational column collation entirely. A module that sets only the relational one
    /// generates a migration whose every file reads correctly and whose columns come out on the server's
    /// default collation, which is accent- and case-insensitive.
    /// </summary>
    [Fact]
    public void Every_collated_MySql_column_also_carries_the_annotation_the_provider_reads()
    {
        foreach (var type in ModuleContextCatalog.Contexts("MySql"))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection("MySql"));
            var assembly = context.GetService<IMigrationsAssembly>();
            foreach (var (id, migrationClass) in assembly.Migrations)
            {
                var target = assembly.CreateMigration(migrationClass, EfProviderNames.MySql).TargetModel;
                foreach (var property in target.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
                {
                    if (property.GetCollation() is not { } collation)
                        continue;
                    Assert.True(
                        Equals(collation, property.FindAnnotation("MySQL:Collation")?.Value),
                        $"{type.Name} migration {id}: {property.DeclaringType.GetTableName()}.{property.Name} declares a relational collation the MySQL provider never reads.");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(CollatingProviders))]
    public void The_columns_the_correctness_argument_rests_on_are_collated(string provider)
    {
        var expected = EfOrdinalCollation.ForProvider(EfRelationalProviderBinding.ExpectedProviderName(provider));
        foreach (var (module, table, column) in MustBeOrdinal)
        {
            var collations = ModuleMigratedCollations(module, provider);
            Assert.True(
                collations.TryGetValue((table, column), out var actual),
                $"{module}/{provider}: {table}.{column} carries no collation in the generated migration.");
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Content_columns_stay_on_the_database_default(string provider)
    {
        foreach (var (module, table, column) in MustNotBeOrdinal)
            Assert.DoesNotContain((table, column), ModuleMigratedCollations(module, provider).Keys);
    }

    /// <summary>A module outside #1837 collates nothing, so the shared declaration cannot leak into one.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void A_module_outside_the_decision_declares_no_collation_at_all(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider).Where(type => !InScope(type)))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            Assert.Empty(MigratedCollations(context, provider));
        }
    }

    private static bool InScope(Type context) =>
        CollatingModules.Any(module => context.Name.StartsWith(module, StringComparison.Ordinal));

    private static IReadOnlyDictionary<(string Table, string Column), string> ModuleMigratedCollations(string module, string provider)
    {
        var type = Assert.Single(ModuleContextCatalog.Contexts(provider), candidate => candidate.Name.StartsWith(module, StringComparison.Ordinal));
        using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
        return MigratedCollations(context, provider);
    }

    /// <summary>What the model asks for, as (table, column) → collation.</summary>
    private static SortedDictionary<(string Table, string Column), string> DeclaredCollations(DbContext context)
    {
        var declared = new SortedDictionary<(string, string), string>();
        foreach (var entity in context.GetService<IDesignTimeModel>().Model.GetEntityTypes())
        {
            if (entity.GetTableName() is not { } table)
                continue;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            foreach (var property in entity.GetProperties())
                if (property.GetCollation(store) is { } collation)
                    declared[(table, property.GetColumnName(store)!)] = collation;
        }

        return declared;
    }

    /// <summary>
    /// What the committed migrations actually create, as (table, column) → collation, folded in migration
    /// order so a later <c>AlterColumn</c> wins — Secrets keeps a chain rather than one Initial.
    /// </summary>
    private static SortedDictionary<(string Table, string Column), string> MigratedCollations(DbContext context, string provider)
    {
        var migrated = new SortedDictionary<(string, string), string>();
        foreach (var operation in Operations(context, provider))
            foreach (var column in Columns(operation))
                if (column.Collation is { } collation)
                    migrated[(column.Table, column.Name)] = collation;
                else
                    migrated.Remove((column.Table, column.Name));

        return migrated;
    }

    private static IEnumerable<ColumnOperation> Columns(MigrationOperation operation) => operation switch
    {
        CreateTableOperation table => table.Columns,
        AddColumnOperation or AlterColumnOperation => [(ColumnOperation)operation],
        _ => []
    };

    private static IEnumerable<MigrationOperation> Operations(DbContext context, string provider)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        Assert.NotEmpty(assembly.Migrations);
        return assembly.Migrations
            .OrderBy(migration => migration.Key, StringComparer.Ordinal)
            .SelectMany(migration => assembly
                .CreateMigration(migration.Value, EfRelationalProviderBinding.ExpectedProviderName(provider))
                .UpOperations);
    }
}
