using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Spec 171 FR-066: the research.md inventory must not be able to grow a feature that depends on an EF
/// module without saying so. Every feature that registers a module's migrations, or that reads a module's
/// context the way <see cref="WorkflowsDashboardEntityFrameworkCoreFeature"/> does, carries a matching
/// <see cref="UsesEfModuleAttribute"/> — and the guard is proved to fire by fixtures that break the rule in
/// each of those two shapes rather than assumed to.
/// </summary>
public sealed class UsesEfModuleGuardTests
{
    /// <summary>
    /// Every assembly that can carry a mapped feature: the eleven module assemblies, plus the two projects
    /// that hold a feature for a module declared elsewhere — the cross-project
    /// <see cref="AspNetCoreIdentityEntityFrameworkCoreFeature"/> and the two-module dashboard feature.
    /// </summary>
    private static readonly Assembly[] FeatureAssemblies =
    [
        .. ModuleContextCatalog.Modules,
        typeof(AspNetCoreIdentityEntityFrameworkCoreFeature).Assembly,
        typeof(WorkflowsDashboardEntityFrameworkCoreFeature).Assembly
    ];

    /// <summary>
    /// research.md's inventory, pinned: every feature the provider-agreement check and the activation guard
    /// must recognise, the module(s) it maps to, and whether it declares a <c>Provider</c> setting of its
    /// own — the distinction FR-036 and FR-037 turn on. The dashboard feature is the one row with no
    /// <c>Provider</c>; it is skipped by the comparison rather than defaulted to Sqlite.
    /// </summary>
    private static readonly (string Feature, string[] Modules, bool DeclaresProvider)[] Inventory =
    [
        ("ActivitiesDesignEntityFrameworkCore", ["Activities.Design"], true),
        ("DiagnosticsOpenTelemetryEntityFrameworkCore", ["Diagnostics.OpenTelemetry"], true),
        ("DiagnosticsStructuredLogsEntityFrameworkCore", ["Diagnostics.StructuredLogs"], true),
        ("Elsa3ImportActivitiesEntityFrameworkCore", ["Elsa3.Activities.Design.Import"], true),
        ("FoundationIdentityAspNetCoreIdentityEntityFrameworkCore", ["Identity.Iam"], true),
        ("IdentityIamEntityFrameworkCore", ["Identity.Iam"], true),
        ("IdentityProviderConfigurationEntityFrameworkCore", ["Identity.ProviderConfiguration"], true),
        ("SecretsEntityFrameworkCore", ["Secrets"], true),
        ("StudioPreferencesEntityFrameworkCore", ["Studio.Preferences"], true),
        ("WorkflowsDashboardEntityFrameworkCore", ["Workflows.Design", "Workflows.Runtime"], false),
        ("WorkflowsDesignEntityFrameworkCore", ["Workflows.Design"], true),
        ("WorkflowsPublishingEntityFrameworkCore", ["Workflows.Publishing"], true),
        ("WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence", ["Workflows.Runtime"], true),
        ("WorkflowsRuntimeAlterationEntityFrameworkCorePersistence", ["Workflows.Runtime"], true),
        ("WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence", ["Workflows.Runtime"], true),
        ("WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence", ["Workflows.Runtime"], true),
        ("WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence", ["Workflows.Runtime.Distributed.CommandTransport"], true),
        ("WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", ["Workflows.Runtime.Distributed.Placement"], true),
        ("WorkflowsRuntimeEntityFrameworkCore", ["Workflows.Runtime"], true),
        ("WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence", ["Workflows.Runtime"], true),
        ("WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence", ["Workflows.Runtime"], true),
        ("WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence", ["Workflows.Runtime"], true)
    ];

    [Fact]
    public void Every_feature_that_depends_on_a_module_names_it_with_UsesEfModule()
    {
        var violations = EfFeatureModuleAudit.Audit(FeatureAssemblies, ModuleContextCatalog.Modules);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    /// <summary>
    /// The audit above only proves nothing is missing an attribute. This proves the mapping itself: the
    /// exact set of features the check sees, under the CShells names a shell enables them by, with the
    /// module(s) each backs — so a renamed feature or a dropped attribute fails here rather than quietly
    /// removing a feature from the comparison.
    /// </summary>
    [Fact]
    public void The_discovered_feature_to_module_map_is_the_research_inventory()
    {
        var discovered = EfProviderAgreement.Discover(FeatureAssemblies)
            .Select(usage => Describe(usage.Feature, usage.Modules, usage.DeclaresProvider))
            .ToArray();

        Assert.Equal(
            Inventory.Select(row => Describe(row.Feature, row.Modules, row.DeclaresProvider)).ToArray(),
            discovered);
    }

    private static string Describe(string feature, IReadOnlyList<string> modules, bool declaresProvider) =>
        $"{feature} -> {string.Join(", ", modules)} (declares Provider: {declaresProvider})";

    /// <summary>
    /// Eight features, one module, each with its own <c>Provider</c>: the exact shape a per-module check
    /// would collapse into one answer and so let one feature mask another (FR-036, ADR 0076 D4).
    /// </summary>
    [Fact]
    public void Workflows_runtime_is_backed_by_eight_separately_configured_features()
    {
        var runtime = EfProviderAgreement.Discover(FeatureAssemblies)
            .Where(usage => usage.Modules.Contains("Workflows.Runtime") && usage.DeclaresProvider)
            .ToArray();

        Assert.Equal(8, runtime.Length);
    }

    /// <summary>
    /// The two shapes that must fail, and the same two shapes annotated, which must not. The undeclared
    /// pair reach their dependency through a separate registration class, exactly as
    /// <c>SecretsEntityFrameworkCoreFeature</c> and the dashboard feature do, so this also proves the guard
    /// is not satisfied by looking at a feature's own <c>ConfigureServices</c> body alone.
    /// </summary>
    [Fact]
    public void The_guard_fires_for_a_feature_that_depends_on_a_module_without_the_attribute()
    {
        var violations = EfFeatureModuleAudit.Audit([typeof(UsesEfModuleGuardTests).Assembly], ModuleContextCatalog.Modules);
        var offenders = violations.Select(violation => (violation.Feature, violation.Module)).ToArray();

        Assert.Contains((typeof(UndeclaredMigrationsFixtureFeature), "Secrets"), offenders);
        Assert.Contains((typeof(UndeclaredContextFixtureFeature), "Workflows.Runtime"), offenders);
        // A [UsesEfModule] naming some other module is not a declaration of this one.
        Assert.Contains((typeof(WrongModuleFixtureFeature), "Secrets"), offenders);
        Assert.DoesNotContain(offenders, offender => offender.Feature == typeof(DeclaredMigrationsFixtureFeature));
        Assert.DoesNotContain(offenders, offender => offender.Feature == typeof(DeclaredContextFixtureFeature));
    }

    [Fact]
    public void The_guard_reports_the_module_and_the_shape_that_established_the_dependency()
    {
        var violations = EfFeatureModuleAudit.Audit([typeof(UsesEfModuleGuardTests).Assembly], ModuleContextCatalog.Modules);

        var registration = Assert.Single(violations, violation => violation.Feature == typeof(UndeclaredMigrationsFixtureFeature));
        Assert.Contains("AddEfModuleMigrations<SecretsDbContext>", registration.Reason);
        Assert.Contains("[UsesEfModule(\"Secrets\")]", registration.ToString());

        var dependency = Assert.Single(violations, violation => violation.Feature == typeof(UndeclaredContextFixtureFeature));
        Assert.Contains("RuntimeDbContext", dependency.Reason);
    }
}

/// <summary>
/// The registrations the guard fixtures reach through, deliberately in a class of their own: the two real
/// shapes this guard has to see through both put the module dependency one call away from the feature.
/// </summary>
internal static class GuardFixtureRegistrations
{
    public static void AddSecretsMigrations(IServiceCollection services, string provider) =>
        services.AddEfModuleMigrations<SecretsDbContext>(provider);

    public static void AddRuntimeProjection(IServiceCollection services) =>
        services.AddScoped<GuardFixtureProjection>(provider => new(provider.GetRequiredService<RuntimeDbContext>()));
}

internal sealed class GuardFixtureProjection(RuntimeDbContext context)
{
    public RuntimeDbContext Context { get; } = context;
}

/// <summary>Shaped like every inventory row that registers migrations — and missing the attribute.</summary>
[ShellFeature(name: "GuardFixtureUndeclaredMigrations")]
internal sealed class UndeclaredMigrationsFixtureFeature : IShellFeature
{
    public string Provider { get; set; } = "Sqlite";

    public void ConfigureServices(IServiceCollection services) => GuardFixtureRegistrations.AddSecretsMigrations(services, Provider);
}

/// <summary>The same shape, declared. The guard must let it through.</summary>
[ShellFeature(name: "GuardFixtureDeclaredMigrations")]
[UsesEfModule("Secrets")]
internal sealed class DeclaredMigrationsFixtureFeature : IShellFeature
{
    public string Provider { get; set; } = "Sqlite";

    public void ConfigureServices(IServiceCollection services) => GuardFixtureRegistrations.AddSecretsMigrations(services, Provider);
}

/// <summary>Declared, but for a different module than the one it registers.</summary>
[ShellFeature(name: "GuardFixtureWrongModule")]
[UsesEfModule("Workflows.Design")]
internal sealed class WrongModuleFixtureFeature : IShellFeature
{
    public string Provider { get; set; } = "Sqlite";

    public void ConfigureServices(IServiceCollection services) => GuardFixtureRegistrations.AddSecretsMigrations(services, Provider);
}

/// <summary>The dashboard shape: it reads a module's context and registers no migrations of its own.</summary>
[ShellFeature(name: "GuardFixtureUndeclaredContext")]
internal sealed class UndeclaredContextFixtureFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => GuardFixtureRegistrations.AddRuntimeProjection(services);
}

/// <summary>The dashboard shape, declared.</summary>
[ShellFeature(name: "GuardFixtureDeclaredContext")]
[UsesEfModule("Workflows.Runtime")]
internal sealed class DeclaredContextFixtureFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) => GuardFixtureRegistrations.AddRuntimeProjection(services);
}
