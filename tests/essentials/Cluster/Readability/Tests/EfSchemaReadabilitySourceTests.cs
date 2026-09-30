using System.Reflection;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Testing;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// Spec 183's FR-019 to FR-022: a host reports one entry for each schema family whose declaration it has loaded, from
/// any load context, at the versions every loaded declaration of it can read, and nothing it has not loaded.
/// </summary>
public sealed class EfSchemaReadabilitySourceTests : IDisposable
{
    private const string Publishing = "Workflows.Publishing";
    private const string Runtime = "Workflows.Runtime";

    private static readonly Assembly PublishingModule = typeof(PublishingSnapshotReviewDbContext).Assembly;
    private static readonly Assembly RuntimeModule = typeof(RuntimeDbContext).Assembly;

    // Pinned to each family's own constants rather than read back from the declarations under test, so a family whose
    // declaration is dropped, or declared at another version, is caught here.
    private static readonly ReadabilityEntry[] PublishingFamilies =
    [
        new(PublishingLedgerEfModule.SchemaFamily, Publishing, [PublishingLedgerEfModule.ContentSchemaVersion], moduleActive: false),
        new(PublishingPolicyProjectionEfModule.SchemaFamily, Publishing, [PublishingPolicyProjectionEfModule.SchemaVersion], moduleActive: false),
        new(PublishingSnapshotReviewEfModule.SchemaFamily, Publishing, [PublishingSnapshotReviewEfModule.SchemaVersion], moduleActive: false)
    ];

    private static readonly ReadabilityEntry[] RuntimeFamilies =
    [
        new(BookmarkStateEfModule.SchemaFamily, Runtime, [BookmarkStateEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeActivationSlotEfModule.SchemaFamily, Runtime, [RuntimeActivationSlotEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeActivityExecutionEfModule.SchemaFamily, Runtime, [RuntimeActivityExecutionEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeArtifactEfModule.SchemaFamily, Runtime, [RuntimeArtifactEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeOperationalStateEfModule.SchemaFamily, Runtime, [RuntimeOperationalStateEfModule.SchemaVersion], moduleActive: false),
        new(RuntimePostCommitOutboxEfModule.SchemaFamily, Runtime, [RuntimePostCommitOutboxEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeSchedulerPoisonEfModule.SchemaFamily, Runtime, [RuntimeSchedulerPoisonEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeTriggerBindingEfModule.SchemaFamily, Runtime, [RuntimeTriggerBindingEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeWorkflowAlterationEfModule.SchemaFamily, Runtime, [RuntimeWorkflowAlterationEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeWorkflowDispatchEfModule.SchemaFamily, Runtime, [RuntimeWorkflowDispatchEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeWorkflowExecutionEfModule.SchemaFamily, Runtime, [RuntimeWorkflowExecutionEfModule.SchemaVersion], moduleActive: false),
        new(RuntimeWorkflowTestScopeEfModule.SchemaFamily, Runtime, [RuntimeWorkflowTestScopeEfModule.SchemaVersion], moduleActive: false)
    ];

    private readonly List<PackageLoadContext> _packages = [];

    [Fact]
    public void A_host_with_two_modules_reports_exactly_their_families_at_their_versions() =>
        Assert.Equal(Ordered([.. PublishingFamilies, .. RuntimeFamilies]), Read(PublishingModule, RuntimeModule).Entries);

    [Fact]
    public void A_host_missing_a_module_does_not_report_its_families()
    {
        var entries = Read(PublishingModule).Entries;

        Assert.Equal(Ordered(PublishingFamilies), entries);
        Assert.DoesNotContain(entries, entry => entry.EfModule == Runtime);
    }

    [Fact]
    public void A_host_that_has_loaded_no_module_reports_an_empty_section() =>
        Assert.Empty(Read().Entries);

    [Fact]
    public async Task What_the_process_has_loaded_is_what_the_host_reports()
    {
        // Naming a type of the module is what loads it; its constants alone compile into this assembly.
        _ = RuntimeModule;

        var entries = (await new EfSchemaReadabilitySource().ReadAsync()).Entries;

        Assert.All(RuntimeFamilies, family => Assert.Contains(family, entries));
    }

    [Fact]
    public void No_entry_names_a_database_or_an_observed_finalized_version_before_a_finalization_record_is_read() =>
        Assert.All(Read(PublishingModule, RuntimeModule).Entries, entry =>
        {
            Assert.Null(entry.DatabaseIdentity);
            Assert.Null(entry.ObservedFinalizedVersion);
        });

    /// <summary>
    /// Spec 183's FR-019 through spec 181's gate: an entry names the database and the finalized version the host read,
    /// but names no database while the host is reading one it has not read before, or once it serves two, since an entry
    /// naming one would not be counted for the other (FR-023) and would let the other finalize what this host cannot read.
    /// </summary>
    [Fact]
    public void An_entry_names_the_database_this_host_read_only_while_that_is_the_one_database_it_serves()
    {
        var observations = new EfSchemaFinalizationObservations();
        var family = RuntimeArtifactEfModule.SchemaFamily;
        ReadabilityEntry Entry() => EfSchemaReadabilitySource.Read(EfSchemaFamilyCatalog.Discover([RuntimeModule]), observations: observations)
            .Entries.Single(entry => entry.Family == family);

        observations.BeginActivation([family]);
        observations.Observe(family, "database-a", "1.0.0");
        Assert.Null(Entry().DatabaseIdentity);
        observations.EndActivation([family]);
        Assert.Equal(("database-a", "1.0.0"), (Entry().DatabaseIdentity, Entry().ObservedFinalizedVersion));
        Assert.Equal([RuntimeArtifactEfModule.SchemaVersion], Entry().ReadableVersions);

        observations.Observe(family, "database-b", "1.0.0");
        Assert.Equal((null, "1.0.0"), (Entry().DatabaseIdentity, Entry().ObservedFinalizedVersion));
    }

    /// <summary>
    /// Spec 183's FR-019, amended 2026-09-30: an entry says whether the family's module is active in this host, so the
    /// backfill's settle condition can leave out a host that only loads the declaration (spec 186, FR-012). Loading it
    /// keeps it in the report, and so in every readability count (FR-022); it stays observed after the module stops.
    /// </summary>
    [Fact]
    public void An_entry_says_its_module_is_active_from_when_a_gate_admits_it_until_every_gate_that_did_has_stopped()
    {
        var observations = new EfSchemaFinalizationObservations();
        var family = RuntimeArtifactEfModule.SchemaFamily;
        var (shellA, shellB) = (new object(), new object());
        ReadabilityEntry Entry() => EfSchemaReadabilitySource.Read(EfSchemaFamilyCatalog.Discover([RuntimeModule]), observations: observations)
            .Entries.Single(entry => entry.Family == family);

        Assert.False(Entry().ModuleActive);

        observations.Observe(family, "database-a", "1.0.0");
        Assert.False(Entry().ModuleActive);

        observations.Activate(shellA, [family]);
        observations.Activate(shellA, [family]);
        observations.Activate(shellB, [family]);
        Assert.True(Entry().ModuleActive);

        observations.Deactivate(shellA, [family]);
        Assert.True(Entry().ModuleActive);

        observations.Deactivate(shellB, [family]);
        var stopped = Entry();
        Assert.False(stopped.ModuleActive);
        Assert.Equal("1.0.0", stopped.ObservedFinalizedVersion);
        Assert.Equal([RuntimeArtifactEfModule.SchemaVersion], stopped.ReadableVersions);
    }

    [Fact]
    public void Every_loaded_declaration_of_a_family_narrows_what_the_host_reports_it_can_read()
    {
        Assert.Equal(["1"], Assert.Single(EfSchemaReadabilitySource.Read([Declaration("Orders", "1"), Declaration("Orders", "1")]).Entries).ReadableVersions);

        // Two generations at different versions whose chains do not reach each other leave a family the host is counted
        // for and can read at no version: never credited with a version it cannot read.
        var generations = Assert.Single(EfSchemaReadabilitySource.Read([Declaration("Orders", "1"), Declaration("Orders", "2")]).Entries);
        Assert.Empty(generations.ReadableVersions);
    }

    /// <summary>A family shared by no single EF module (spec 180, FR-001) reports an entry that names none, end to end
    /// through <see cref="ReadabilityEntry.EfModule"/>, rather than a sentinel value a B5 or fleet-view consumer might
    /// mistake for a real module name.</summary>
    [Fact]
    public void A_shared_family_reports_an_entry_naming_no_module()
    {
        var entry = Assert.Single(EfSchemaReadabilitySource.Read([Declaration("Finalization", "1", module: null)]).Entries);

        Assert.Null(entry.EfModule);
        Assert.Equal(["1"], entry.ReadableVersions);
    }

    /// <summary>The finalization tables' family (spec 180, FR-001 extension; spec 181) is declared in
    /// Elsa.Persistence.EntityFramework itself, shared by no single EF module, so a host that has loaded only that
    /// assembly - none of the EF modules that call <see cref="EfSchemaFinalization.MapSchemaFinalization"/> - still
    /// reports it, naming no module.</summary>
    [Fact]
    public void A_host_that_has_loaded_the_finalization_assembly_reports_its_shared_family_with_no_module() =>
        Assert.Contains(
            new ReadabilityEntry(EfSchemaFinalization.SchemaFamily, null, [EfSchemaFinalization.SchemaVersion], moduleActive: false),
            Read(typeof(EfSchemaFinalization).Assembly).Entries);

    [Fact]
    public void Declarations_that_disagree_on_the_owning_module_report_no_readable_version_and_log_the_disagreement()
    {
        var logger = new RecordingLogger<EfSchemaReadabilitySource>();

        var section = EfSchemaReadabilitySource.Read([Declaration("Orders", "1", "Sales"), Declaration("Orders", "1", "Billing")], logger);

        // Isolated, not dropped: the malformed family still gets an entry, reading no version - the conservative
        // direction, since ReadsSchemaVersion counts and fails a host for a family it cannot read at all - rather than
        // silently missing from the report the way a thrown exception or a filtered-out family would.
        var entry = Assert.Single(section.Entries);
        Assert.Equal("Orders", entry.Family);
        Assert.Empty(entry.ReadableVersions);

        var error = Assert.Single(logger.Entries, logged => logged.Level == LogLevel.Error);
        Assert.Contains("'Sales'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Billing'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orders", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A declaration naming a module disagreeing with one declaring the family shared (no module) is the same
    /// isolation path as two disagreeing modules: it still picks a deterministic (here, <see langword="null"/>) module
    /// for the entry rather than failing the whole report.</summary>
    [Fact]
    public void A_declaration_naming_a_module_disagreeing_with_a_shared_declaration_still_picks_a_module_deterministically()
    {
        var section = EfSchemaReadabilitySource.Read([Declaration("Orders", "1", "Sales"), Declaration("Orders", "1", module: null)]);

        var entry = Assert.Single(section.Entries);
        Assert.Null(entry.EfModule);
        Assert.Empty(entry.ReadableVersions);
    }

    /// <summary>Bite-proof: an implementation that drops a malformed family instead of isolating it still passes every
    /// other assertion above, since an empty section and a single-entry section both contain no violation. This is the
    /// one assertion that only an isolated (not a dropped) family satisfies.</summary>
    [Fact]
    public void A_family_that_disagrees_on_the_owning_module_still_appears_beside_a_well_formed_one()
    {
        var section = EfSchemaReadabilitySource.Read(
            [Declaration("Orders", "1", "Sales"), Declaration("Orders", "1", "Billing"), Declaration("Invoices", "1")]);

        Assert.Equal(2, section.Entries.Count);
        Assert.Contains(section.Entries, entry => entry.Family == "Orders" && entry.ReadableVersions.Count == 0);
        Assert.Contains(section.Entries, entry => entry.Family == "Invoices" && entry.ReadableVersions.SequenceEqual(["1"]));
    }

    [Fact]
    public void An_unchanged_host_publishes_an_equal_report_whatever_order_its_assemblies_load_in()
    {
        ReadabilityEntry[] expected = [new("Invoices", "Sales", ["2"], moduleActive: false), new("Orders", "Sales", ["1"], moduleActive: false)];

        Assert.Equal(expected, EfSchemaReadabilitySource.Read([Declaration("Orders", "1"), Declaration("Invoices", "2")]).Entries);
        Assert.Equal(expected, EfSchemaReadabilitySource.Read([Declaration("Invoices", "2"), Declaration("Orders", "1")]).Entries);
    }

    /// <summary>
    /// A package that carries its own copy of the persistence assembly declares its families with an attribute type
    /// that is not the host's. Matching on the host's type would skip them and look like success.
    /// </summary>
    [Fact]
    public async Task A_family_loaded_in_another_load_context_with_its_own_attribute_type_is_reported()
    {
        var family = new SyntheticFamily($"Packaged{Guid.NewGuid():N}", "Packaged", "4");
        var package = LoadPackage("Packaged", family);

        Assert.NotSame(typeof(EfSchemaFamilyAttribute), package.GetCustomAttributesData().First(attribute => attribute.AttributeType.Name == nameof(EfSchemaFamilyAttribute)).AttributeType);
        Assert.Empty(package.GetCustomAttributes<EfSchemaFamilyAttribute>());

        var entry = Assert.Single((await new EfSchemaReadabilitySource().ReadAsync()).Entries, entry => entry.Family == family.Name);
        Assert.Equal(new ReadabilityEntry(family.Name, "Packaged", ["4"], moduleActive: false), entry);
    }

    [Fact]
    public async Task Two_loaded_generations_of_a_package_report_only_what_both_read()
    {
        var family = $"Generational{Guid.NewGuid():N}";
        LoadPackage("Generational", new SyntheticFamily(family, "Generational", "1"));
        LoadPackage("Generational", new SyntheticFamily(family, "Generational", "2"));

        var entry = Assert.Single((await new EfSchemaReadabilitySource().ReadAsync()).Entries, entry => entry.Family == family);

        Assert.Empty(entry.ReadableVersions);
    }

    public void Dispose() => _packages.ForEach(package => package.Unload());

    /// <summary>The section a host that has loaded exactly <paramref name="loaded"/> reports.</summary>
    private static ReadabilitySection Read(params Assembly[] loaded) => EfSchemaReadabilitySource.Read(EfSchemaFamilyCatalog.Discover(loaded));

    private static ReadabilityEntry[] Ordered(IEnumerable<ReadabilityEntry> entries) => [.. entries.OrderBy(entry => entry.Family, StringComparer.Ordinal)];

    private static EfSchemaFamilyDescriptor Declaration(string family, string version, string? module = "Sales") =>
        new(family, module, version, typeof(EfSchemaReadabilitySourceTests).Assembly);

    private Assembly LoadPackage(string module, SyntheticFamily family)
    {
        var package = new PackageLoadContext();
        _packages.Add(package);
        return package.Load(SyntheticSchemaFamilies.Image(module, [module], family));
    }
}
