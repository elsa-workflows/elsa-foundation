using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Spec 185 RQ-001: per EF module and provider context, the migration ids #1976 freezes as that context's
/// baseline. An entry present with an empty list means "no baseline recorded" (FR-002): every migration the
/// context has is post-freeze. A missing entry is FR-020a's own failure — a supported provider context the
/// manifest never mentions at all.
/// </summary>
internal sealed record FreezeManifest(IReadOnlyDictionary<(string Module, string Provider), IReadOnlyList<string>> Baselines)
{
    public static readonly FreezeManifest Empty = new(new Dictionary<(string, string), IReadOnlyList<string>>());

    public bool HasEntry(string module, string provider) => Baselines.ContainsKey((module, provider));

    public IReadOnlyList<string> BaselineOf(string module, string provider) =>
        Baselines.TryGetValue((module, provider), out var ids) ? ids : [];

    /// <summary>Builds a manifest recording every migration id <paramref name="idsByModuleAndProvider"/> lists as that context's baseline.</summary>
    public static FreezeManifest Recording(IEnumerable<((string Module, string Provider) Key, IReadOnlyList<string> Ids)> idsByModuleAndProvider) =>
        new(idsByModuleAndProvider.ToDictionary(entry => entry.Key, entry => entry.Ids));
}

/// <summary>
/// Spec 185 FR-003: the real scan fails when the freeze manifest is missing or unreadable, naming the path it
/// expected. It never treats a missing manifest as "nothing is post-freeze".
/// </summary>
internal sealed class FreezeManifestMissingException(string message) : InvalidOperationException(message);

/// <summary>
/// Reads the freeze manifest #1976 adds (spec 185 RQ-001). #1976 has not happened yet (spec 185, "Current
/// state"): nothing here writes that file, only reads it, and reading it today is expected to fail — proving
/// FR-003 rather than working around it.
/// </summary>
internal static class FreezeManifestReader
{
    /// <summary>
    /// Repo-relative path #1976 is expected to add its manifest at, beside the generator this spec's research
    /// names as the tool that will grow an "add" mode for post-freeze migrations
    /// (<c>tools/ef/generate-module-migrations.sh</c>). Not fixed by RQ-001; #1976 may choose otherwise, in
    /// which case only this constant moves.
    /// </summary>
    public const string ExpectedPath = "tools/ef/migration-baseline-manifest.json";

    public static FreezeManifest Load(string repoRelativePath)
    {
        var fullPath = Path.IsPathRooted(repoRelativePath) ? repoRelativePath : RepoPath.Resolve(repoRelativePath);
        if (!File.Exists(fullPath))
            throw new FreezeManifestMissingException(
                $"The expand-only migration guard's real scan expected a freeze manifest at '{repoRelativePath}' " +
                "(#1976, spec 185 RQ-001) and found none. Until #1976 freezes a module's baseline there, this " +
                "guard does not run over it — a missing manifest is never treated as \"nothing is post-freeze\".");

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        var baselines = new Dictionary<(string, string), IReadOnlyList<string>>();
        foreach (var moduleProperty in document.RootElement.EnumerateObject())
            foreach (var providerProperty in moduleProperty.Value.EnumerateObject())
                baselines[(moduleProperty.Name, providerProperty.Name)] =
                    providerProperty.Value.EnumerateArray().Select(id => id.GetString()!).ToArray();

        return new FreezeManifest(baselines);
    }
}

/// <summary>What one run of <see cref="ExpandOnlyMigrationScanner"/> examined (spec 185 FR-018).</summary>
internal sealed record ExpandOnlyMigrationScanReport(
    int Modules,
    int ProviderContextsExamined,
    int ProviderContextsSkipped,
    int BaselineMigrations,
    int PostFreezeMigrations,
    int OperationsClassified,
    IReadOnlyList<string> OptOutsHonoured,
    IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// The real scan (spec 185 FR-001 to FR-002, FR-004 to FR-020b): every migration of every provider context of
/// every module <paramref name="moduleAssemblies"/> declares, classified through
/// <see cref="ExpandOnlyMigrationGuard"/> exactly as <see cref="ExpandOnlyMigrationGuardTests"/>'s fixtures are
/// (FR-022) — the same <see cref="IMigrationsAssembly.CreateMigration"/> path
/// <c>OrdinalCollationMigrationTests</c> already reads operations through (FR-004).
/// </summary>
internal static class ExpandOnlyMigrationScanner
{
    private const string Remedy =
        "split the change across two versions: add the new shape alongside the old one now, and remove the old " +
        "one later in a contracting migration with an opt-out (spec 185 FR-016), once no finalized version still " +
        "reads what it removes.";

    /// <summary>
    /// Spec 185's shared enumeration seam: every (module, provider) pair of <paramref name="moduleAssemblies"/>'s
    /// declared modules crossed with <paramref name="providers"/>, with the declared provider context or
    /// <c>null</c> when the module marks that provider unsupported. <see cref="Scan"/> and any test building a
    /// manifest from today's real migrations enumerate through this one place, so they can never drift apart on
    /// which modules and provider contexts exist.
    /// </summary>
    public static IEnumerable<(EfModuleDescriptor Descriptor, string Provider, Type? ContextType)> EnumerateModuleProviderContexts(
        IReadOnlyList<Assembly> moduleAssemblies, IReadOnlyList<string> providers)
    {
        foreach (var descriptor in EfModuleCatalog.Discover(moduleAssemblies))
            foreach (var provider in providers)
                yield return (descriptor, provider, descriptor.ProviderContext(provider));
    }

    /// <summary>
    /// FR-013, User Story 3: whether one post-freeze migration's opt-out is what let it pass — never recorded
    /// for a migration whose opt-out itself failed (an unlisted violation, or a stale entry), which belongs in
    /// the report's failures rather than in <see cref="ExpandOnlyMigrationScanReport.OptOutsHonoured"/>.
    /// </summary>
    internal static bool ShouldRecordOptOutHonoured(ExpandOnlyMigrationOptOutAttribute? optOut, ExpandOnlyMigrationResult result) =>
        optOut is not null && result.Passed;

    public static ExpandOnlyMigrationScanReport Scan(IReadOnlyList<Assembly> moduleAssemblies, IReadOnlyList<string> providers, FreezeManifest manifest)
    {
        var modules = EfModuleCatalog.Discover(moduleAssemblies).Count;
        var providerContextsExamined = 0;
        var providerContextsSkipped = 0;
        var baselineMigrations = 0;
        var postFreezeMigrations = 0;
        var operationsClassified = 0;
        var optOutsHonoured = new List<string>();
        var failures = new List<string>();

        foreach (var (descriptor, provider, contextType) in EnumerateModuleProviderContexts(moduleAssemblies, providers))
        {
            if (contextType is null)
            {
                // FR-001: a provider context the declaration marks unsupported is skipped and reported as skipped.
                providerContextsSkipped++;
                continue;
            }

            if (!manifest.HasEntry(descriptor.Name, provider))
            {
                // FR-020a: every supported provider context must have a baseline entry. None at all is its own failure.
                failures.Add($"{descriptor.Name}/{provider}: no baseline entry in the freeze manifest.");
                providerContextsExamined++;
                continue;
            }

            providerContextsExamined++;
            var baseline = manifest.BaselineOf(descriptor.Name, provider);

            using var context = ModuleContextCatalog.Create(contextType, ModuleContextCatalog.PlaceholderConnection(provider));
            var assembly = context.GetService<IMigrationsAssembly>();
            var ordered = assembly.Migrations.OrderBy(migration => migration.Key, StringComparer.Ordinal).ToArray();

            // FR-020b: a baseline id that names no migration this context actually has — a typo, or a
            // renamed or deleted baseline — never gets the chance to quietly stop counting as post-freeze.
            var orderedIds = ordered.Select(migration => migration.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var staleId in baseline.Where(id => !orderedIds.Contains(id)))
                failures.Add($"{descriptor.Name}/{provider}: the freeze manifest's baseline names migration " +
                             $"'{staleId}', which does not exist in this context (renamed, deleted, or a typo'd id).");

            var postFreeze = ordered.Where(migration => !baseline.Contains(migration.Key, StringComparer.Ordinal)).ToArray();

            // FR-020b's independent floor: the post-freeze count must equal the context's total migration
            // count minus the manifest's baseline count for it — computed from raw counts, not from the
            // same membership test that built postFreeze above, so a defect there still surfaces here.
            if (postFreeze.Length != ordered.Length - baseline.Count)
                failures.Add($"{descriptor.Name}/{provider}: post-freeze migration count ({postFreeze.Length}) " +
                             $"does not equal the context's migration count ({ordered.Length}) minus the " +
                             $"manifest's baseline count ({baseline.Count}) for this context.");

            baselineMigrations += ordered.Length - postFreeze.Length;
            postFreezeMigrations += postFreeze.Length;

            foreach (var (id, migrationType) in postFreeze)
            {
                IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations;
                try
                {
                    // FR-004: what EF will execute, never source text. FR-005: Down is never read.
                    operations = assembly.CreateMigration(migrationType, EfRelationalProviderBinding.ExpectedProviderName(provider)).UpOperations;
                }
                catch (Exception exception)
                {
                    // FR-017: a migration the guard cannot build fails the guard, naming it. Never skipped.
                    failures.Add($"{descriptor.Name}/{provider} {id}: could not be built without a database ({exception.Message}).");
                    continue;
                }

                operationsClassified += operations.Count;
                var optOut = migrationType.GetCustomAttribute<ExpandOnlyMigrationOptOutAttribute>();
                var result = ExpandOnlyMigrationGuard.Evaluate(operations, optOut);

                if (ShouldRecordOptOutHonoured(optOut, result))
                    optOutsHonoured.Add($"{descriptor.Name}/{provider} {id}: {optOut!.ReviewReference} — {optOut.Reason}");

                if (!result.Passed)
                    failures.Add(FormatFailure(descriptor.Name, provider, id, result));
            }
        }

        return new ExpandOnlyMigrationScanReport(
            modules,
            providerContextsExamined,
            providerContextsSkipped,
            baselineMigrations,
            postFreezeMigrations,
            operationsClassified,
            optOutsHonoured,
            failures);
    }

    private static string FormatFailure(string module, string provider, string migrationId, ExpandOnlyMigrationResult result)
    {
        var reasons = new List<string>();
        if (result.UnlistedViolations.Count > 0)
            reasons.Add($"violates expand-only: {string.Join(", ", result.UnlistedViolations)}");
        if (result.StaleOptOutEntries.Count > 0)
            reasons.Add($"opt-out lists violations that do not occur: {string.Join(", ", result.StaleOptOutEntries)}");
        if (result is { HasOptOut: true, Violations.Count: 0 })
            reasons.Add("carries an opt-out but has no violations to permit");

        return $"{module}/{provider} {migrationId}: {string.Join("; ", reasons)}. {Remedy}";
    }

    /// <summary>
    /// Spec 185 FR-019's other independent enumeration: every <c>[assembly: EfModule(...)]</c> declared in the
    /// source of <paramref name="roots"/> (repo-relative), read as text rather than by loading assemblies, so a
    /// module <see cref="ModuleContextCatalog.Modules"/> forgot to add an anchor for still turns up here.
    /// </summary>
    public static IReadOnlyCollection<string> ModuleNamesDeclaredInSource(params string[] roots)
    {
        var pattern = new Regex(@"\[assembly:\s*EfModule\(\s*""([^""]+)""", RegexOptions.Compiled);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var directory = RepoPath.Resolve(root);
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
                foreach (Match match in pattern.Matches(File.ReadAllText(file)))
                    names.Add(match.Groups[1].Value);
        }

        return names;
    }
}

internal static class RepoPath
{
    public static string Resolve(string repoRelativePath) => Path.Join(Root, repoRelativePath);

    public static readonly string Root = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
