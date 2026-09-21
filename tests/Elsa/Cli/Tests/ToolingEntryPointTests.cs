using Acme.Widgets;
using Elsa.Cli.Worker;
using Elsa.Persistence.EntityFramework.Tooling;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// Binding the host's own tooling entry point, and the refusal for a host whose persistence build predates
/// it (FR-010).
/// </summary>
public sealed class ToolingEntryPointTests
{
    /// <summary>
    /// The decision is the entry point's presence, not a version string — a version cannot say whether a
    /// build carries a type — but the message still names the version the host pins and a version known to
    /// carry it, because those are the two numbers an operator needs to act.
    /// </summary>
    [Fact]
    public void A_persistence_build_with_no_entry_point_is_refused_naming_both_versions()
    {
        var refusal = Assert.Throws<WorkerRefusal>(() => ToolingEntryPoint.Resolve(typeof(object).Assembly, "4.0.0-preview.1", "4.0.0-preview.999"));

        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.Equal("host-tooling-entry-point-missing", refusal.Code);
        Assert.Contains("4.0.0-preview.1", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("4.0.0-preview.999", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_pins_no_version_at_all_still_gets_a_message_it_can_act_on()
    {
        var refusal = Assert.Throws<WorkerRefusal>(() => ToolingEntryPoint.Resolve(typeof(object).Assembly, pinnedVersion: null, "4.0.0-preview.999"));

        Assert.Contains("version unknown", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosts_own_binding_table_decides_the_canonical_provider_and_its_engine_package()
    {
        var entryPoint = ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly, "4.0.0-preview.1", "4.0.0-preview.1");

        Assert.Equal("PostgreSql", entryPoint.CanonicalProvider("postgres"));
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", entryPoint.ProviderPackageId("PostgreSql"));
    }

    [Fact]
    public void An_unknown_provider_is_a_usage_refusal_rather_than_a_reflection_failure()
    {
        var entryPoint = ToolingEntryPoint.Resolve(typeof(EfToolingHost).Assembly, "4.0.0-preview.1", "4.0.0-preview.1");

        var refusal = Assert.Throws<WorkerRefusal>(() => entryPoint.CanonicalProvider("Oracle"));

        Assert.Equal(ToolExitCode.Refusal, refusal.ExitCode);
        Assert.Equal("unknown-provider", refusal.Code);
    }

    /// <summary>
    /// The fixture module is a third-party one, and the entry point it is discovered through is the host's
    /// own: nothing about this assembly is known to Elsa (spec 171 User Story 6).
    /// </summary>
    [Fact]
    public void The_third_party_fixture_declares_a_module_the_hosts_own_catalog_discovers()
    {
        var descriptor = Assert.Single(Elsa.Persistence.EntityFramework.EfModuleCatalog.Discover([typeof(WidgetsDbContext).Assembly]));

        Assert.Equal("Acme.Widgets", descriptor.Name);
        Assert.Equal("__EFMigrationsHistory_AcmeWidgets", descriptor.HistoryTableName);
        Assert.Null(descriptor.ProviderContext("MySql"));
    }
}
