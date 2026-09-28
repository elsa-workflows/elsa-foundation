using System.Reflection;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

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
        new(PublishingLedgerEfModule.SchemaFamily, Publishing, [PublishingLedgerEfModule.ContentSchemaVersion]),
        new(PublishingPolicyProjectionEfModule.SchemaFamily, Publishing, [PublishingPolicyProjectionEfModule.SchemaVersion]),
        new(PublishingSnapshotReviewEfModule.SchemaFamily, Publishing, [PublishingSnapshotReviewEfModule.SchemaVersion])
    ];

    private static readonly ReadabilityEntry[] RuntimeFamilies =
    [
        new(BookmarkStateEfModule.SchemaFamily, Runtime, [BookmarkStateEfModule.SchemaVersion]),
        new(RuntimeActivationSlotEfModule.SchemaFamily, Runtime, [RuntimeActivationSlotEfModule.SchemaVersion]),
        new(RuntimeActivityExecutionEfModule.SchemaFamily, Runtime, [RuntimeActivityExecutionEfModule.SchemaVersion]),
        new(RuntimeArtifactEfModule.SchemaFamily, Runtime, [RuntimeArtifactEfModule.SchemaVersion]),
        new(RuntimeOperationalStateEfModule.SchemaFamily, Runtime, [RuntimeOperationalStateEfModule.SchemaVersion]),
        new(RuntimePostCommitOutboxEfModule.SchemaFamily, Runtime, [RuntimePostCommitOutboxEfModule.SchemaVersion]),
        new(RuntimeSchedulerPoisonEfModule.SchemaFamily, Runtime, [RuntimeSchedulerPoisonEfModule.SchemaVersion]),
        new(RuntimeTriggerBindingEfModule.SchemaFamily, Runtime, [RuntimeTriggerBindingEfModule.SchemaVersion]),
        new(RuntimeWorkflowAlterationEfModule.SchemaFamily, Runtime, [RuntimeWorkflowAlterationEfModule.SchemaVersion]),
        new(RuntimeWorkflowDispatchEfModule.SchemaFamily, Runtime, [RuntimeWorkflowDispatchEfModule.SchemaVersion]),
        new(RuntimeWorkflowExecutionEfModule.SchemaFamily, Runtime, [RuntimeWorkflowExecutionEfModule.SchemaVersion]),
        new(RuntimeWorkflowTestScopeEfModule.SchemaFamily, Runtime, [RuntimeWorkflowTestScopeEfModule.SchemaVersion])
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

    [Fact]
    public void Every_loaded_declaration_of_a_family_narrows_what_the_host_reports_it_can_read()
    {
        Assert.Equal(["1"], Assert.Single(EfSchemaReadabilitySource.Read([Declaration("Orders", "1"), Declaration("Orders", "1")]).Entries).ReadableVersions);

        // Until the chain lets one generation read the other's version, two generations at different versions leave a
        // family the host is counted for and can read at no version: never credited with a version it cannot read.
        var generations = Assert.Single(EfSchemaReadabilitySource.Read([Declaration("Orders", "1"), Declaration("Orders", "2")]).Entries);
        Assert.Empty(generations.ReadableVersions);
    }

    [Fact]
    public void Declarations_that_disagree_on_the_owning_module_are_refused_rather_than_reported()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            EfSchemaReadabilitySource.Read([Declaration("Orders", "1", "Sales"), Declaration("Orders", "1", "Billing")]));

        Assert.Contains("'Sales'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'Billing'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unchanged_host_publishes_an_equal_report_whatever_order_its_assemblies_load_in()
    {
        ReadabilityEntry[] expected = [new("Invoices", "Sales", ["2"]), new("Orders", "Sales", ["1"])];

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
        Assert.Equal(new ReadabilityEntry(family.Name, "Packaged", ["4"]), entry);
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

    private static EfSchemaFamilyDescriptor Declaration(string family, string version, string module = "Sales") =>
        new(family, module, version, typeof(EfSchemaReadabilitySourceTests).Assembly);

    private Assembly LoadPackage(string module, SyntheticFamily family)
    {
        var package = new PackageLoadContext();
        _packages.Add(package);
        return package.Load(SyntheticSchemaFamilies.Image(module, [module], family));
    }
}
