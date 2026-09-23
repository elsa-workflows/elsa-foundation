using System.Reflection;
using CShells.Features;
using CShells;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

public sealed class EfPersistenceResourceEnrollmentTests
{
    private static readonly Assembly[] FeatureAssemblies =
    [
        .. ModuleContextCatalog.Modules,
        typeof(AspNetCoreIdentityEntityFrameworkCoreFeature).Assembly,
        typeof(WorkflowsDashboardEntityFrameworkCoreFeature).Assembly
    ];

    private static readonly (string Feature, string Module, string Context)[] Expected =
    [
        ("WorkflowsRuntimeEntityFrameworkCore", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsRuntimeAlterationEntityFrameworkCorePersistence", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence", "Workflows.Runtime", "RuntimeDbContext"),
        ("WorkflowsDesignEntityFrameworkCore", "Workflows.Design", "WorkflowsDesignDbContext"),
        ("ActivitiesDesignEntityFrameworkCore", "Activities.Design", "ActivitiesDesignDbContext"),
        ("WorkflowsPublishingEntityFrameworkCore", "Workflows.Publishing", "PublishingSnapshotReviewDbContext"),
        ("DiagnosticsStructuredLogsEntityFrameworkCore", "Diagnostics.StructuredLogs", "StructuredLogsDbContext"),
        ("DiagnosticsOpenTelemetryEntityFrameworkCore", "Diagnostics.OpenTelemetry", "EfOpenTelemetryDbContext")
    ];

    [Fact]
    public void Only_the_reviewed_thirteen_features_are_enrolled_with_declared_module_ownership()
    {
        var discovered = EfPersistenceParticipantCatalog.Discover(FeatureAssemblies);

        Assert.Equal(Expected.Select(x => x.Feature).Order(StringComparer.Ordinal),
            discovered.Select(x => x.FeatureId).Order(StringComparer.Ordinal));
        foreach (var (feature, module, context) in Expected)
        {
            var participant = Assert.Single(discovered, item => item.FeatureId == feature);
            Assert.Equal(module, Assert.Single(participant.ModuleNames));
            Assert.EndsWith($".{context}", participant.ContextIdentity, StringComparison.Ordinal);
            Assert.True(participant.DeclaresProvider);
            Assert.False(participant.HasOpaqueConfigurator);
        }
    }

    [Fact]
    public void Explicit_opaque_metadata_is_reported_without_constructing_a_feature()
    {
        var assemblies = new[] { typeof(ThrowingEnrollmentProbe).Assembly, typeof(RuntimeDbContext).Assembly };

        var discovered = EfPersistenceParticipantCatalog.Discover(assemblies, ["ThrowingEnrollmentProbe"]);

        var probe = Assert.Single(discovered, item => item.FeatureId == "ThrowingEnrollmentProbe");
        Assert.Equal("Workflows.Runtime", Assert.Single(probe.ModuleNames));
        Assert.EndsWith(".RuntimeDbContext", probe.ContextIdentity, StringComparison.Ordinal);
        Assert.True(probe.HasOpaqueConfigurator);
    }

    [Fact]
    public void Public_preparation_materializes_only_provider_and_connection_name_before_feature_construction()
    {
        var context = PreparationContext();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["Elsa:Persistence:DefaultResource"] = "primary"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.True(result.HasApplicableResource);
        Assert.Empty(result.RefusalCodes);
        Assert.Equal("PostgreSql", result.Patch.ConfigurationData["WorkflowsRuntimeEntityFrameworkCore:Provider"]);
        Assert.Equal("Shared", result.Patch.ConfigurationData["WorkflowsRuntimeEntityFrameworkCore:ConnectionName"]);
        Assert.Equal(2, result.Patch.ConfigurationData.Count);
    }

    [Fact]
    public void Public_preparation_refusal_returns_no_partial_patch_or_secret()
    {
        var context = PreparationContext(new Dictionary<string, string?>
        {
            ["WorkflowsRuntimeEntityFrameworkCore:Provider"] = "Sqlite"
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["ConnectionStrings:Shared"] = "Host=secret-canary;Password=never-export"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.Contains("resource-legacy-conflict", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
        Assert.DoesNotContain("secret-canary", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Public_preparation_refuses_opaque_configurator_without_constructing_feature()
    {
        var context = new ShellSettingsPreparationContext(new ShellId("default"),
            new Dictionary<string, string?>(), ["ThrowingEnrollmentProbe"], [], [],
            [new ShellFeaturePreparationDescriptor("ThrowingEnrollmentProbe", [],
                typeof(ThrowingEnrollmentProbe), true)], ["ThrowingEnrollmentProbe"], [], []);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["Elsa:Persistence:DefaultResource"] = "primary"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.Equal(["resource-configurator-unsupported"], result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    [Fact]
    public void Resource_selection_for_known_id_without_owning_startup_type_refuses()
    {
        var context = new ShellSettingsPreparationContext(new ShellId("default"),
            new Dictionary<string, string?>(), ["WorkflowsRuntimeEntityFrameworkCore"], [], [],
            [new ShellFeaturePreparationDescriptor("WorkflowsRuntimeEntityFrameworkCore", [], null, false)],
            ["WorkflowsRuntimeEntityFrameworkCore"], [], []);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["Elsa:Persistence:DefaultResource"] = "primary"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.Contains("resource-ownership-unresolved", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    [Fact]
    public void Selected_resource_with_unsupported_field_refuses_without_patching()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["Elsa:Persistence:Resources:primary:Schema"] = "other",
            ["Elsa:Persistence:DefaultResource"] = "primary"
        }).Build();

        var result = EfPersistencePreparation.Prepare(PreparationContext(), configuration);

        Assert.Equal(["resource-definition-invalid"], result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    private static ShellSettingsPreparationContext PreparationContext(IReadOnlyDictionary<string, string?>? values = null) =>
        new(new ShellId("default"), values ?? new Dictionary<string, string?>(),
            ["WorkflowsRuntimeEntityFrameworkCore"], [], [],
            [new ShellFeaturePreparationDescriptor("WorkflowsRuntimeEntityFrameworkCore", [],
                typeof(RuntimeEntityFrameworkCoreFeature), false)],
            ["WorkflowsRuntimeEntityFrameworkCore"], [], []);
}

[ShellFeature(name: "ThrowingEnrollmentProbe")]
[UsesEfModule("Workflows.Runtime")]
[EfPersistenceResourceParticipant]
internal sealed class ThrowingEnrollmentProbe : IShellFeature
{
    public ThrowingEnrollmentProbe() => throw new InvalidOperationException("Metadata discovery must not construct features.");

    public string Provider { get; set; } = "Sqlite";

    public void ConfigureServices(IServiceCollection services) =>
        throw new InvalidOperationException("Metadata discovery must not configure features.");
}
