using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The post-finalization backfill against the model every first-party module's context builds, on every provider (spec
/// 186): every stamped table is marked <see cref="EfSchemaContentAddressedAttribute"/> exactly when its family names it
/// content-addressed, and the tables holding executables and executable activity templates are among them, so the
/// backfill never rewrites them (FR-010a, FR-010b); and every stamped table of every family can be selected in batches that
/// resume after the last key, which a key the database cannot order would make impossible (FR-005).
/// </summary>
/// <remarks>
/// The rule reads two explicit declarations, the entity's marker and its family's list, and fails when they disagree in
/// either direction. It infers nothing from a table's name, so a renamed table keeps its declaration, and a table copied
/// from a content-addressed one carries the marker into a family that must then name it. <see cref="ExecutableTables"/>
/// pins the ones FR-010b names, and fails when one of them stops being a stamped table rather than passing on a stale name.
/// </remarks>
public sealed class EfSchemaContentAddressedDeclarationTests
{
    /// <summary>
    /// The first-party stamped tables that hold executables and executable activity templates, and the claims keyed by a
    /// template's hash (FR-010b; ADR 0038).
    /// </summary>
    private static readonly string[] ExecutableTables =
    [
        "elsa_runtime_workflow_executable",
        "elsa_runtime_executable_activity_template",
        "elsa_runtime_executable_activity_template_hash_claim"
    ];

    /// <summary>The stamped tables of every first-party family, at least: what the scans below must reach, or they pass vacuously.</summary>
    private const int MinimumStampedTables = 60;

    public static TheoryData<string> Providers() => [.. ModuleContextCatalog.Providers];

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_stamped_table_is_marked_content_addressed_exactly_when_its_family_names_it(string provider)
    {
        var violations = new List<string>();
        var stamped = new HashSet<string>(StringComparer.Ordinal);
        var contentAddressed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, context, families) in Modules(provider, ModuleContextCatalog.PlaceholderConnection(provider)))
        {
            violations.AddRange(ContentAddressedRules.Violations(context.Model, families, type.Name));
            stamped.UnionWith(ContentAddressedRules.Stamped(context.Model).Select(entity => entity.GetTableName()!));
            contentAddressed.UnionWith(ContentAddressedRules.Declared(context.Model, families));
        }

        Assert.True(violations.Count == 0,
            "Every stamped table is marked [EfSchemaContentAddressed] with its reason exactly when the family that owns it names it " +
            "content-addressed, and every table a family names content-addressed is one of its stamped tables (spec 186, FR-010b):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct().Order(StringComparer.Ordinal)));
        Assert.True(stamped.Count >= MinimumStampedTables, $"Expected the stamped tables of every first-party family; found {stamped.Count}.");
        Assert.Empty(ExecutableTables.Except(stamped, StringComparer.Ordinal));
        Assert.Superset(ExecutableTables.ToHashSet(StringComparer.Ordinal), contentAddressed);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void The_backfill_can_select_every_stamped_table_of_every_family_in_batches(string provider)
    {
        var tables = 0;
        var violations = new List<string>();
        foreach (var (type, context, families) in Modules(provider, ModuleContextCatalog.PlaceholderConnection(provider)))
        {
            foreach (var chain in families.Chains)
            {
                try
                {
                    tables += EfSchemaStampedTable.Of(context.Model, families, families.DeclarationOf(chain.Family)).Count;
                }
                catch (NotSupportedException exception)
                {
                    violations.Add($"{type.Name}, family '{chain.Family}': {exception.Message}");
                }
            }
        }

        Assert.True(violations.Count == 0, "Every stamped table of every family can be selected in batches that resume after the last key (spec 186, FR-005):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
        Assert.True(tables >= MinimumStampedTables, $"Expected the stamped tables of every first-party family; found {tables}.");
    }

    /// <summary>
    /// The selections the backfill makes of every stamped table, keyset and count, translated and run against every
    /// module's own schema on SQLite, so a key the engine cannot compare fails here rather than in a host's first run.
    /// The provider tests run the synthetic family's on the three server engines.
    /// </summary>
    [Fact]
    public async Task The_backfills_selections_of_every_stamped_table_translate_and_run()
    {
        var selected = 0;
        foreach (var (_, context, families) in Modules("Sqlite", "Data Source=:memory:"))
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();
            foreach (var chain in families.Chains)
            {
                foreach (var table in EfSchemaStampedTable.Of(context.Model, families, families.DeclarationOf(chain.Family)))
                {
                    var key = context.Model.FindEntityType(table.Entity)!.FindPrimaryKey()!.Properties
                        .Select(property => property.ClrType == typeof(string) ? "" : Activator.CreateInstance(property.ClrType))
                        .ToArray();
                    Assert.Empty(await table.PageAsync(context, EfSchemaStampFilter.NotIn(chain.ReadableVersions), key, 10, CancellationToken.None));
                    Assert.Empty(await table.PageAsync(context, EfSchemaStampFilter.In(chain.ReadableVersions), null, 10, CancellationToken.None));
                    Assert.Equal(0, await table.CountAsync(context, EfSchemaStampFilter.NotIn(chain.ReadableVersions), CancellationToken.None));
                    selected++;
                }
            }
        }

        Assert.True(selected >= MinimumStampedTables, $"Expected to select from the stamped tables of every first-party family; selected from {selected}.");
    }

    [Theory]
    [InlineData(true, false, "is marked [EfSchemaContentAddressed], but its family 'Synthetic' does not name it content-addressed")]
    [InlineData(false, true, "is named content-addressed by its family 'Synthetic', but is not marked [EfSchemaContentAddressed]")]
    [InlineData(true, true, null)]
    [InlineData(false, false, null)]
    public void Detector_flags_a_marker_and_a_declaration_that_disagree_and_accepts_them_agreeing(bool marked, bool declared, string? expected)
    {
        var entity = marked ? typeof(MarkedReceipt) : typeof(UnmarkedReceipt);
        var families = EfSchemaModuleFamilies.FromDeclarations("Synthetic",
        [
            new EfSchemaFamilyDescriptor("Synthetic", "Synthetic", "1", entity.Assembly) { ContentAddressed = declared ? [entity] : [] }
        ]);

        var violations = ContentAddressedRules.Violations(SyntheticModel(entity), families, "SyntheticDbContext").ToArray();

        if (expected is null)
            Assert.Empty(violations);
        else
            Assert.Contains(violations, violation => violation.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every first-party module context of <paramref name="provider"/> that owns a schema family, on
    /// <paramref name="connectionString"/>, each disposed once the caller moves on to the next.
    /// </summary>
    private static IEnumerable<(Type Type, DbContext Context, EfSchemaModuleFamilies Families)> Modules(string provider, string connectionString)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            if (EfSchemaModuleFamilies.ForContext(type) is not { } families)
                continue;
            using var context = ModuleContextCatalog.Create(type, connectionString);
            yield return (type, context, families);
        }
    }

    private static IModel SyntheticModel(Type entity)
    {
        var builder = new ModelBuilder(SqliteConventionSetBuilder.Build());
        builder.Entity(entity, table =>
        {
            table.ToTable("synthetic_receipt");
            table.HasKey(nameof(UnmarkedReceipt.Hash));
        });
        return (IModel)builder.FinalizeModel();
    }

    private class UnmarkedReceipt
    {
        public string Hash { get; set; } = "";
        public string SchemaVersion { get; set; } = "";
        public string ContentJson { get; set; } = "";
    }

    [EfSchemaContentAddressed("Keyed by the hash of its content.")]
    private sealed class MarkedReceipt : UnmarkedReceipt;
}

/// <summary>FR-010b's rule over one model, so the detector can be pinned on a synthetic model.</summary>
internal static class ContentAddressedRules
{
    public static IEnumerable<string> Violations(IReadOnlyModel model, EfSchemaModuleFamilies families, string context)
    {
        var stamped = Stamped(model).ToArray();
        foreach (var entity in stamped)
        {
            var marker = (EfSchemaContentAddressedAttribute?)Attribute.GetCustomAttribute(entity.ClrType, typeof(EfSchemaContentAddressedAttribute), inherit: false);
            var family = families.FamilyOf(entity.ClrType)?.Family;
            var declared = family is not null && families.DeclarationOf(family).ContentAddressed.Any(type => type.IsAssignableFrom(entity.ClrType));
            var table = $"{context}: '{entity.GetTableName()}' of {entity.ClrType.Name}";
            if (marker is not null && !declared)
                yield return $"{table} is marked [EfSchemaContentAddressed], but its family '{family ?? "(none)"}' does not name it content-addressed.";
            if (declared && marker is null)
                yield return $"{table} is named content-addressed by its family '{family}', but is not marked [EfSchemaContentAddressed] with the reason its key follows from its content.";
            if (marker is not null && string.IsNullOrWhiteSpace(marker.Reason))
                yield return $"{table} is marked [EfSchemaContentAddressed] with no reason.";
        }

        foreach (var chain in families.Chains)
        {
            foreach (var type in families.DeclarationOf(chain.Family).ContentAddressed.Where(type => stamped.All(entity => !type.IsAssignableFrom(entity.ClrType))))
                yield return $"{context}: family '{chain.Family}' names {type.Name} content-addressed, which is none of its stamped tables.";
        }
    }

    /// <summary>The tables of <paramref name="model"/> a family of <paramref name="families"/> names content-addressed.</summary>
    public static IEnumerable<string> Declared(IReadOnlyModel model, EfSchemaModuleFamilies families) =>
        Stamped(model)
            .Where(entity => families.FamilyOf(entity.ClrType) is { } family &&
                             families.DeclarationOf(family.Family).ContentAddressed.Any(type => type.IsAssignableFrom(entity.ClrType)))
            .Select(entity => entity.GetTableName()!);

    public static IEnumerable<IReadOnlyEntityType> Stamped(IReadOnlyModel model) =>
        model.GetEntityTypes().Where(entity => entity.GetTableName() is not null && EfSchemaModuleFamilies.IsStamped(entity));
}
