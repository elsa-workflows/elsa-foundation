using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Persistence.EntityFramework.Tooling;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfToolingTargetSelectionTests
{
    [Fact]
    public void Alias_resources_share_a_declared_group_and_from_host_includes_both_modules()
    {
        var prepared = Prepared(
            [Owner("First", "A", "primary", "PostgreSql", "Shared"),
             Owner("Second", "B", "alias", "Npgsql", "Shared")]);

        var selected = EfToolingTargetSelection.Select(prepared, ["A", "B"],
            new EfToolingSelection { Kind = EfToolingSelection.FromHostKind }, "primary");

        Assert.Equal(["A", "B"], selected);
    }

    [Fact]
    public void Diagnostics_resource_selects_only_its_two_modules_and_refuses_primary_module_requests()
    {
        var prepared = Prepared(
            [Owner("Runtime", "Workflows.Runtime", "primary", "PostgreSql", "Primary"),
             Owner("Logs", "Diagnostics.StructuredLogs", "diagnostics", "PostgreSql", "Diagnostics"),
             Owner("Telemetry", "Diagnostics.OpenTelemetry", "diagnostics", "PostgreSql", "Diagnostics")]);
        string[] discovered = ["Workflows.Runtime", "Diagnostics.StructuredLogs", "Diagnostics.OpenTelemetry"];

        var diagnostics = EfToolingTargetSelection.Select(prepared, discovered,
            new EfToolingSelection { Kind = EfToolingSelection.FromHostKind }, "diagnostics");
        var primary = EfToolingTargetSelection.Select(prepared, discovered,
            new EfToolingSelection { Kind = EfToolingSelection.FromHostKind }, "primary");

        Assert.Equal(["Diagnostics.OpenTelemetry", "Diagnostics.StructuredLogs"], diagnostics);
        Assert.Equal(["Workflows.Runtime"], primary);
        foreach (var selection in new[]
                 {
                     new EfToolingSelection { Kind = EfToolingSelection.ModulesKind,
                         Modules = ["Diagnostics.StructuredLogs", "Workflows.Runtime"] },
                     new EfToolingSelection { Kind = EfToolingSelection.AllKind }
                 })
            Assert.Equal("resource-target-scope", Assert.Throws<EfToolingRefusal>(() =>
                EfToolingTargetSelection.Select(prepared, discovered, selection, "diagnostics")).Code);
    }

    [Fact]
    public void Explicit_and_all_selection_refuse_a_module_with_any_owner_outside_the_group()
    {
        var prepared = Prepared(
            [Owner("First", "A", "primary", "PostgreSql", "Shared"),
             Owner("Other", "A", "diagnostics", "PostgreSql", "Diagnostics"),
             Owner("Second", "B", "primary", "PostgreSql", "Shared")]);

        foreach (var selection in new[]
                 {
                     new EfToolingSelection { Kind = EfToolingSelection.ModulesKind, Modules = ["A"] },
                     new EfToolingSelection { Kind = EfToolingSelection.AllKind }
                 })
            Assert.Equal("resource-target-scope", Assert.Throws<EfToolingRefusal>(() =>
                EfToolingTargetSelection.Select(prepared, ["A", "B"], selection, "primary")).Code);
    }

    [Fact]
    public void A_module_with_an_unenrolled_enabled_owner_cannot_be_selected_by_resource()
    {
        var prepared = Prepared([Owner("First", "A", "primary", "PostgreSql", "Shared")]) with
        {
            HostFeatureUsages =
            [
                new EfFeatureModuleUsage("First", typeof(EfToolingTargetSelectionTests), ["A"], true),
                new EfFeatureModuleUsage("Unenrolled", typeof(EfToolingTargetSelectionTests), ["A"], true)
            ]
        };

        Assert.Equal("resource-target-scope", Assert.Throws<EfToolingRefusal>(() =>
            EfToolingTargetSelection.Select(prepared, ["A"],
                new EfToolingSelection { Kind = EfToolingSelection.FromHostKind }, "primary")).Code);
    }

    [Fact]
    public void A_read_only_bridge_without_a_provider_does_not_own_the_module_target()
    {
        var prepared = Prepared([Owner("First", "A", "primary", "PostgreSql", "Shared")]) with
        {
            HostFeatureUsages =
            [
                new EfFeatureModuleUsage("First", typeof(EfToolingTargetSelectionTests), ["A"], true),
                new EfFeatureModuleUsage("DashboardReader", typeof(EfToolingTargetSelectionTests), ["A"], false)
            ]
        };

        var selected = EfToolingTargetSelection.Select(prepared, ["A"],
            new EfToolingSelection { Kind = EfToolingSelection.FromHostKind }, "primary");

        Assert.Equal(["A"], selected);
    }

    private static EfPersistencePreparationResult Prepared(IReadOnlyList<PersistenceParticipantResolution> owners)
    {
        var source = new PersistenceSourceProvenance("root", "configuration", false, true);
        var resources = owners.Where(owner => owner.ResourceName is not null)
            .GroupBy(owner => owner.ResourceName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
                new PersistenceResourceDefinition(group.Key,
                    new PersistenceAuthoredValue(PersistencePresence.Value, group.First().Provider, source),
                    new PersistenceAuthoredValue(PersistencePresence.Value, group.First().ConnectionName, source), source),
                StringComparer.OrdinalIgnoreCase);
        return new EfPersistencePreparationResult(
            new ShellSettingsPreparationResult(new Dictionary<string, string?>()), true, [], [], [])
        {
            ResolvedParticipants = owners,
            ResourceDefinitions = resources
        };
    }

    private static PersistenceParticipantResolution Owner(
        string feature, string module, string resource, string provider, string reference) =>
        new(new EnrolledPersistenceParticipant(feature, [module], module, true, false),
            PersistenceSelectionKind.ShellBinding, resource, provider, reference,
            new PersistenceSourceProvenance("shell", "configuration", false, true), []);
}
