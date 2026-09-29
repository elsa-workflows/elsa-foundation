using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The post-finalization backfill against the model every first-party module's context builds, on every provider (spec
/// 186): every stamped table that holds executables is declared content-addressed by the family that owns it, so the
/// backfill never rewrites it (FR-010a, FR-010b); and every stamped table of every family can be selected in batches that
/// resume after the last key, which a key the database cannot order would make impossible (FR-005).
/// </summary>
public sealed class EfSchemaContentAddressedDeclarationTests
{
    /// <summary>
    /// Stamped tables whose names mention executables but whose rows' identity is not derived from their own content, so
    /// the backfill may rewrite them, each with why. A new table holding executables in a new format (spec 180, FR-028)
    /// is not here, so it fails the rule until its family names it content-addressed.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RewritableExecutableTables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["elsa_runtime_workflow_executable_coordination"] = "per-publish coordination of an executable, keyed by artifact id, not by a hash of its own content",
        ["elsa_runtime_workflow_executable_source_reference"] = "a per-publish fact naming an executable, keyed by its source reference",
        ["elsa_runtime_executable_activity_template_hash_claim"] = "a claim that a template hash names a template, keyed by that hash, which its own content does not produce"
    };

    public static TheoryData<string> Providers() => [.. ModuleContextCatalog.Providers];

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_stamped_table_holding_executables_is_declared_content_addressed_by_its_family(string provider)
    {
        var violations = new List<string>();
        var contentAddressed = new List<string>();
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            var families = EfSchemaModuleFamilies.ForContext(type);
            if (families is null)
                continue;
            violations.AddRange(ContentAddressedRules.Violations(context.Model, families, RewritableExecutableTables, type.Name));
            contentAddressed.AddRange(ContentAddressedRules.Declared(context.Model, families));
        }

        Assert.True(violations.Count == 0,
            "Every stamped table holding executables or executable activity templates is named content-addressed by the family that owns it, " +
            "and every table a family names content-addressed is one of its stamped tables (spec 186, FR-010b):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct().Order(StringComparer.Ordinal)));
        Assert.Superset(
            new HashSet<string> { "elsa_runtime_workflow_executable", "elsa_runtime_executable_activity_template" },
            contentAddressed.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void The_backfill_can_select_every_stamped_table_of_every_family_in_batches(string provider)
    {
        var tables = 0;
        var violations = new List<string>();
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            if (EfSchemaModuleFamilies.ForContext(type) is not { } families)
                continue;
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
        Assert.True(tables >= 60, $"Expected the stamped tables of every first-party family; found {tables}.");
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
        foreach (var type in ModuleContextCatalog.Contexts("Sqlite"))
        {
            await using var context = ModuleContextCatalog.Create(type, "Data Source=:memory:");
            if (EfSchemaModuleFamilies.ForContext(type) is not { } families)
                continue;
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

        Assert.True(selected >= 60, $"Expected to select from the stamped tables of every first-party family; selected from {selected}.");
    }

    [Theory]
    [InlineData(false, "'synthetic_executable' of SyntheticExecutable holds executables")]
    [InlineData(true, null)]
    public void Detector_flags_an_undeclared_table_that_holds_executables_and_accepts_a_declared_one(bool declared, string? expected)
    {
        var families = EfSchemaModuleFamilies.FromDeclarations("Synthetic",
        [
            new EfSchemaFamilyDescriptor("Synthetic", "Synthetic", "1", typeof(SyntheticExecutable).Assembly)
            {
                ContentAddressed = declared ? [typeof(SyntheticExecutable)] : []
            }
        ]);

        var violations = ContentAddressedRules.Violations(SyntheticModel(), families, RewritableExecutableTables, "SyntheticDbContext").ToArray();

        if (expected is null)
            Assert.Empty(violations);
        else
            Assert.Contains(violations, violation => violation.Contains(expected, StringComparison.Ordinal));
    }

    private static IModel SyntheticModel()
    {
        var builder = new ModelBuilder(SqliteConventionSetBuilder.Build());
        builder.Entity<SyntheticExecutable>(entity =>
        {
            entity.ToTable("synthetic_executable");
            entity.HasKey(row => row.Hash);
        });
        return (IModel)builder.FinalizeModel();
    }

    private sealed class SyntheticExecutable
    {
        public string Hash { get; set; } = "";
        public string SchemaVersion { get; set; } = "";
        public string ContentJson { get; set; } = "";
    }
}

/// <summary>FR-010b's rule over one model, so the detector can be pinned on a synthetic model.</summary>
internal static class ContentAddressedRules
{
    public static IEnumerable<string> Violations(
        IReadOnlyModel model,
        EfSchemaModuleFamilies families,
        IReadOnlyDictionary<string, string> rewritable,
        string context)
    {
        var stamped = Stamped(model).ToArray();
        foreach (var entity in stamped.Where(entity => entity.GetTableName()!.Contains("executable", StringComparison.OrdinalIgnoreCase)))
        {
            if (rewritable.ContainsKey(entity.GetTableName()!))
                continue;
            var family = families.FamilyOf(entity.ClrType);
            if (family is null || !families.DeclarationOf(family.Family).ContentAddressed.Any(type => type.IsAssignableFrom(entity.ClrType)))
                yield return $"{context}: '{entity.GetTableName()}' of {entity.ClrType.Name} holds executables, but its family " +
                             $"'{family?.Family ?? "(none)"}' does not name it content-addressed.";
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

    private static IEnumerable<IReadOnlyEntityType> Stamped(IReadOnlyModel model) =>
        model.GetEntityTypes().Where(entity => entity.GetTableName() is not null && EfSchemaModuleFamilies.IsStamped(entity));
}
