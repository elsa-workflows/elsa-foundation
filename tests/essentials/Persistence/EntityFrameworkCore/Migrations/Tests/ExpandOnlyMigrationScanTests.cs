using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Spec 185 (elsa-workflows/elsa-foundation#2104, B8) User Story 4: "the scan cannot pass by examining
/// nothing". Everything here runs <see cref="ExpandOnlyMigrationScanner"/> over the real, first-party module
/// set <see cref="ModuleContextCatalog"/> already carries, not a synthetic stand-in.
/// </summary>
public sealed class ExpandOnlyMigrationScanTests
{
    /// <summary>
    /// FR-003, SC-004, User Story 4 Acceptance Scenario 3. #1976 has not happened (spec 185, "Current state"):
    /// no baseline is frozen and no freeze manifest exists, and ADR 0078 says this guard does not apply to a
    /// real database before then. This is that state, proven rather than assumed: the real scan, pointed at the
    /// real expected path, refuses to guess and never quietly reports success over zero migrations.
    /// </summary>
    [Fact]
    public void The_real_scan_fails_naming_the_path_when_the_freeze_manifest_is_missing()
    {
        var failure = Assert.Throws<FreezeManifestMissingException>(() => FreezeManifestReader.Load(FreezeManifestReader.ExpectedPath));

        Assert.Contains(FreezeManifestReader.ExpectedPath, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-002's "no baseline recorded" fallback is what makes the guard switch on automatically: the moment
    /// #1976 freezes a module and provider context's migrations at the time as its baseline, every migration
    /// recorded there stops being scanned, and only what ships afterwards is checked. This proves the whole
    /// pipeline end to end over every real module today, as if #1976 had just landed: every current migration,
    /// for every module and provider, is recorded as baseline, and the scan finds nothing left to classify and
    /// nothing to fail — the exact state #1976's freeze is meant to leave behind.
    /// </summary>
    [Fact]
    public void Every_first_party_module_passes_once_every_migration_it_has_today_is_recorded_as_its_baseline()
    {
        var manifest = FreezeEveryCurrentMigrationAsBaseline();

        var report = ExpandOnlyMigrationScanner.Scan(ModuleContextCatalog.Modules, ModuleContextCatalog.Providers, manifest);

        Assert.True(report.Passed, string.Join("\n", report.Failures));
        Assert.Equal(13, report.Modules);
        Assert.Equal(0, report.PostFreezeMigrations);
        Assert.Equal(0, report.OperationsClassified);
        Assert.True(report.BaselineMigrations > 0);
        Assert.True(report.ProviderContextsExamined > 0);
    }

    /// <summary>
    /// FR-020a: a supported provider context the manifest never mentions at all fails, distinctly from an entry
    /// that is merely empty (FR-002).
    /// </summary>
    [Fact]
    public void A_supported_provider_context_missing_from_the_manifest_entirely_fails()
    {
        var manifest = FreezeEveryCurrentMigrationAsBaseline();
        var withoutOneEntry = new FreezeManifest(
            manifest.Baselines.Where(entry => entry.Key != ("Secrets", "Sqlite")).ToDictionary(entry => entry.Key, entry => entry.Value));

        var report = ExpandOnlyMigrationScanner.Scan(ModuleContextCatalog.Modules, ModuleContextCatalog.Providers, withoutOneEntry);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure => failure.Contains("Secrets/Sqlite", StringComparison.Ordinal) && failure.Contains("no baseline entry", StringComparison.Ordinal));
    }

    /// <summary>
    /// Today's answer to "Secrets already has incremental migrations such as SchemaVersionStamp — do they pass
    /// the guard, or does the spec say why not?" (spec 185 Terms, "Baseline": "for Secrets the historical chain
    /// it already keeps"). Scanned as post-freeze — the FR-002 fallback a manifest entry with no ids recorded
    /// yet produces, exactly the shape #1976 leaves behind for a module or provider it has not reached —
    /// Secrets' pre-guard chain genuinely violates expand-only on every provider but SQLite:
    /// <c>SchemaVersionStamp</c> adds a non-nullable, defaulted column (a default does not give "unset" for an
    /// older host's write) on all four, and <c>WidenLookupKeys</c> and <c>OrdinalCollation</c> retype existing
    /// columns — OrdinalCollation's SQL Server migration also rebuilds the primary key — on SQL Server,
    /// PostgreSQL and MySQL. Spec 185's own Terms is what makes this expected rather than a defect this guard
    /// needs to work around: #1976 folds this whole chain into Secrets' baseline, at which point none of it is
    /// scanned again, proved clean in that exact shape by the test above.
    /// </summary>
    [Fact]
    public void Secrets_kept_chain_scanned_as_post_freeze_today_shows_why_1976_must_fold_it_into_the_baseline()
    {
        var descriptor = Assert.Single(EfModuleCatalog.Discover(ModuleContextCatalog.Modules), d => d.Name == "Secrets");
        var manifest = new FreezeManifest(ModuleContextCatalog.Providers
            .Where(provider => descriptor.ProviderContext(provider) is not null)
            .ToDictionary(provider => ("Secrets", provider), IReadOnlyList<string> (_) => []));

        var report = ExpandOnlyMigrationScanner.Scan([typeof(SecretsDbContext).Assembly], ModuleContextCatalog.Providers, manifest);

        Assert.NotEmpty(report.Failures);
        // Every failure here is a genuine content violation (FR-016), never FR-020a's structural "no baseline
        // entry at all" — this manifest gave every supported provider context an entry, just an empty one.
        Assert.DoesNotContain(report.Failures, failure => failure.Contains("no baseline entry", StringComparison.Ordinal));
        Assert.All(report.Failures, failure => Assert.Contains("violates expand-only", failure, StringComparison.Ordinal));
    }

    /// <summary>
    /// The other half of FR-019's two independent enumerations, provable without #1976's manifest: every
    /// <c>[assembly: EfModule(...)]</c> declared in the source of all three roots FR-001 names must equal what
    /// <see cref="EfModuleCatalog.Discover"/> finds by loading assemblies — a module <see cref="ModuleContextCatalog.Modules"/>
    /// forgot to add an anchor for (research.md, "Noticed and left alone") fails here rather than being silently skipped.
    /// </summary>
    [Fact]
    public void Every_EfModule_declared_in_source_across_all_three_roots_is_one_EfModuleCatalog_discovers()
    {
        var declaredInSource = ExpandOnlyMigrationScanner.ModuleNamesDeclaredInSource("src/essentials", "src/extensions", "src/apps");
        var discovered = EfModuleCatalog.Discover(ModuleContextCatalog.Modules).Select(descriptor => descriptor.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            discovered.OrderBy(name => name, StringComparer.Ordinal),
            declaredInSource.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void FreezeManifestReader_parses_baseline_ids_per_module_and_provider()
    {
        var path = Path.Combine(Path.GetTempPath(), $"expand-only-guard-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Secrets":{"Sqlite":["20260910210210_Initial"]}}""");
        try
        {
            var manifest = FreezeManifestReader.Load(path);

            Assert.Equal(["20260910210210_Initial"], manifest.BaselineOf("Secrets", "Sqlite"));
            Assert.True(manifest.HasEntry("Secrets", "Sqlite"));
            Assert.False(manifest.HasEntry("Secrets", "SqlServer"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Builds a manifest that records every migration every real module and provider context has today as that context's baseline.</summary>
    private static FreezeManifest FreezeEveryCurrentMigrationAsBaseline()
    {
        var descriptors = EfModuleCatalog.Discover(ModuleContextCatalog.Modules);
        var entries = new Dictionary<(string, string), IReadOnlyList<string>>();
        foreach (var descriptor in descriptors)
        foreach (var provider in ModuleContextCatalog.Providers)
        {
            var contextType = descriptor.ProviderContext(provider);
            if (contextType is null)
                continue;
            using var context = ModuleContextCatalog.Create(contextType, ModuleContextCatalog.PlaceholderConnection(provider));
            var assembly = context.GetService<IMigrationsAssembly>();
            entries[(descriptor.Name, provider)] = assembly.Migrations.Keys.ToArray();
        }

        return new FreezeManifest(entries);
    }
}
