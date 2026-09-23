using System.Reflection;
using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
