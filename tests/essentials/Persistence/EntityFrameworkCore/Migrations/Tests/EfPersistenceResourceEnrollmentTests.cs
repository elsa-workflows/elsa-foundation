using System.Reflection;
using System.Reflection.Emit;
using CShells.Features;
using CShells;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
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
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["ConnectionStrings:Shared"] = "Host=localhost;Database=elsa;Username=test;Password=secret-canary"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.True(result.HasApplicableResource);
        Assert.Empty(result.RefusalCodes);
        Assert.Equal("PostgreSql", result.Patch.ConfigurationData["WorkflowsRuntimeEntityFrameworkCore:Provider"]);
        Assert.Equal("Shared", result.Patch.ConfigurationData["WorkflowsRuntimeEntityFrameworkCore:ConnectionName"]);
        Assert.Equal(2, result.Patch.ConfigurationData.Count);
    }

    [Fact]
    public void Preparation_ignores_unrelated_dynamic_module_metadata()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Unrelated.InvalidEfModule.{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var constructor = typeof(EfModuleAttribute).GetConstructor([typeof(string), typeof(Type)])!;
        var history = typeof(EfModuleAttribute).GetProperty(nameof(EfModuleAttribute.HistoryModule))!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor,
            ["Unrelated.Invalid", typeof(object)], [history], ["Bad.Name"]));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["ConnectionStrings:Shared"] = "Data Source=shared.db"
        }).Build();

        var result = EfPersistencePreparation.Prepare(PreparationContext(), configuration);

        Assert.Empty(result.RefusalCodes);
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

    [Theory]
    [InlineData("Sqlite", "resource-definition-invalid")]
    [InlineData("UnknownProvider", "resource-context-conflict")]
    public void Resource_preflight_refuses_missing_connection_or_unknown_provider_before_patching(
        string provider, string code)
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = provider,
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Missing",
            ["Elsa:Persistence:DefaultResource"] = "primary"
        };
        if (provider == "UnknownProvider")
            values["ConnectionStrings:Missing"] = "Data Source=unused.db";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var result = EfPersistencePreparation.Prepare(PreparationContext(), configuration);

        Assert.Contains(code, result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    [Theory]
    [InlineData("Host=localhost;Database=one", "Host=localhost;Database=two", false, false)]
    [InlineData("Host=localhost;Database=one", "Host=localhost;Database=one", false, true)]
    [InlineData("Host=localhost;Database=one", "Host=localhost;Database=one", true, false)]
    public void Features_sharing_runtime_context_must_agree_on_physical_target_and_pooling(
        string firstConnection, string secondConnection, bool firstPooling, bool secondPooling)
    {
        var context = RuntimePairContext(new Dictionary<string, string?>
            {
                ["WorkflowsRuntimeEntityFrameworkCore:Pooling"] = firstPooling.ToString(),
                ["WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence:Pooling"] = secondPooling.ToString(),
                ["Elsa:Persistence:Bindings:WorkflowsRuntimeEntityFrameworkCore"] = "first",
                ["Elsa:Persistence:Bindings:WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence"] = "second"
            });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:first:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:first:ConnectionName"] = "One",
            ["Elsa:Persistence:Resources:second:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:second:ConnectionName"] = "Two",
            ["ConnectionStrings:One"] = firstConnection,
            ["ConnectionStrings:Two"] = secondConnection
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.Contains("resource-context-conflict", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
        Assert.DoesNotContain(firstConnection, System.Text.Json.JsonSerializer.Serialize(result));
        Assert.DoesNotContain(secondConnection, System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Features_sharing_runtime_context_must_agree_on_schema_even_with_one_default_resource()
    {
        var context = RuntimePairContext(new Dictionary<string, string?>
        {
            ["WorkflowsRuntimeEntityFrameworkCore:Schema"] = "runtime_first",
            ["WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence:Schema"] = "runtime_second"
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["ConnectionStrings:Shared"] = "Host=localhost;Database=unused"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);
        var offline = EfPersistencePreparation.Prepare(context, configuration, verifyConnectionValues: false);

        Assert.Contains("resource-context-conflict", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
        Assert.Contains("resource-context-conflict", offline.RefusalCodes);
        Assert.Empty(offline.Patch.ConfigurationData);
    }

    [Fact]
    public void Invalid_migration_policy_refuses_selected_resource_without_patching()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["ConnectionStrings:Shared"] = "Host=localhost;Database=unused",
            ["Elsa:Persistence:EntityFramework:Migrate:Policy"] = "InvalidPolicy"
        }).Build();

        var result = EfPersistencePreparation.Prepare(PreparationContext(), configuration);

        Assert.Contains("resource-context-conflict", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    [Fact]
    public void Different_connection_names_can_share_the_shells_effective_runtime_target()
    {
        var context = RuntimePairContext(new Dictionary<string, string?>
            {
                ["Elsa:Persistence:Bindings:WorkflowsRuntimeEntityFrameworkCore"] = "first",
                ["Elsa:Persistence:Bindings:WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence"] = "second",
                ["ConnectionStrings:One"] = "Data Source=same.db"
            });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:first:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:first:ConnectionName"] = "One",
            ["Elsa:Persistence:Resources:second:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:second:ConnectionName"] = "Two",
            ["ConnectionStrings:One"] = "Data Source=root-would-differ.db",
            ["ConnectionStrings:Two"] = "Data Source=same.db"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.Empty(result.RefusalCodes);
        Assert.Equal(4, result.Patch.ConfigurationData.Count);
    }

    [Fact]
    public void Resource_selected_runtime_feature_cannot_split_a_legacy_runtime_feature()
    {
        var context = RuntimePairContext(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:WorkflowsRuntimeEntityFrameworkCore"] = "first"
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:first:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:first:ConnectionName"] = "Selected",
            ["ConnectionStrings:Selected"] = "Data Source=selected.db",
            ["ConnectionStrings:Elsa"] = "Data Source=legacy.db"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, configuration);

        Assert.Contains("resource-context-conflict", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    [Theory]
    [InlineData("Data Source=design.db", false)]
    [InlineData("Data Source=activities.db", true)]
    public void Activity_upgrade_design_contexts_must_share_a_target_without_constraining_publishing(
        string workflowsConnection, bool expectedValid)
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:ActivitiesDesignEntityFrameworkCore"] = "activities",
            ["Elsa:Persistence:Bindings:WorkflowsDesignEntityFrameworkCore"] = "workflows",
            ["Elsa:Persistence:Bindings:WorkflowsPublishingEntityFrameworkCore"] = "publishing"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:activities:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:activities:ConnectionName"] = "Activities",
            ["Elsa:Persistence:Resources:workflows:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:workflows:ConnectionName"] = "Workflows",
            ["Elsa:Persistence:Resources:publishing:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:publishing:ConnectionName"] = "Publishing",
            ["ConnectionStrings:Activities"] = "Data Source=activities.db",
            ["ConnectionStrings:Workflows"] = workflowsConnection,
            ["ConnectionStrings:Publishing"] = "Data Source=publishing.db"
        }).Build();

        var result = EfPersistencePreparation.Prepare(ActivityUpgradeContext(values), configuration);

        Assert.Equal(expectedValid, result.RefusalCodes.Count == 0);
        if (expectedValid)
            Assert.Equal(6, result.Patch.ConfigurationData.Count);
        else
        {
            Assert.Contains("resource-context-conflict", result.RefusalCodes);
            Assert.Empty(result.Patch.ConfigurationData);
        }
        Assert.DoesNotContain("Data Source=", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Offline_activity_upgrade_with_distinct_references_reports_unverified_affinity()
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:ActivitiesDesignEntityFrameworkCore"] = "activities",
            ["Elsa:Persistence:Bindings:WorkflowsDesignEntityFrameworkCore"] = "workflows",
            ["Elsa:Persistence:Bindings:WorkflowsPublishingEntityFrameworkCore"] = "publishing"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:activities:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:activities:ConnectionName"] = "Activities",
            ["Elsa:Persistence:Resources:workflows:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:workflows:ConnectionName"] = "Workflows",
            ["Elsa:Persistence:Resources:publishing:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:publishing:ConnectionName"] = "Publishing"
        }).Build();

        var result = EfPersistencePreparation.Prepare(ActivityUpgradeContext(values), configuration,
            verifyConnectionValues: false);

        Assert.Empty(result.RefusalCodes);
        Assert.Contains("target-affinity-unverified", result.UnresolvedCodes);
        Assert.Equal(6, result.Patch.ConfigurationData.Count);
    }

    [Fact]
    public void Separate_design_targets_without_the_publishing_upgrade_store_are_not_rejected()
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:ActivitiesDesignEntityFrameworkCore"] = "activities",
            ["Elsa:Persistence:Bindings:WorkflowsDesignEntityFrameworkCore"] = "workflows"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:activities:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:activities:ConnectionName"] = "Activities",
            ["Elsa:Persistence:Resources:workflows:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:workflows:ConnectionName"] = "Workflows",
            ["ConnectionStrings:Activities"] = "Data Source=activities.db",
            ["ConnectionStrings:Workflows"] = "Data Source=workflows.db"
        }).Build();

        var result = EfPersistencePreparation.Prepare(ActivityUpgradeContext(values, includePublishing: false), configuration);

        Assert.Empty(result.RefusalCodes);
        Assert.Equal(4, result.Patch.ConfigurationData.Count);
    }

    [Fact]
    public void Resource_selected_activity_design_checks_the_legacy_workflow_design_transaction_target()
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Bindings:ActivitiesDesignEntityFrameworkCore"] = "activities",
            ["Elsa:Persistence:Bindings:WorkflowsPublishingEntityFrameworkCore"] = "publishing"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:activities:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:activities:ConnectionName"] = "Activities",
            ["Elsa:Persistence:Resources:publishing:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:publishing:ConnectionName"] = "Publishing",
            ["ConnectionStrings:Activities"] = "Data Source=activities.db",
            ["ConnectionStrings:Publishing"] = "Data Source=publishing.db",
            ["ConnectionStrings:Elsa"] = "Data Source=legacy-design.db"
        }).Build();

        var result = EfPersistencePreparation.Prepare(ActivityUpgradeContext(values), configuration);

        Assert.Contains("resource-context-conflict", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
    }

    private static ShellSettingsPreparationContext ActivityUpgradeContext(
        IReadOnlyDictionary<string, string?> values, bool includePublishing = true)
    {
        var features = new List<ShellFeaturePreparationDescriptor>
        {
            new("ActivitiesDesignEntityFrameworkCore", [], typeof(ActivitiesDesignEntityFrameworkCoreFeature), false),
            new("WorkflowsDesignEntityFrameworkCore", [], typeof(WorkflowsDesignEntityFrameworkCoreFeature), false)
        };
        if (includePublishing)
            features.Add(new("WorkflowsPublishingEntityFrameworkCore", [], typeof(PublishingEntityFrameworkCoreFeature), false));
        var ids = features.Select(x => x.Id).ToArray();
        return new ShellSettingsPreparationContext(new ShellId("default"), values, ids, [], [], features, ids, [], []);
    }

    private static ShellSettingsPreparationContext PreparationContext(IReadOnlyDictionary<string, string?>? values = null) =>
        new(new ShellId("default"), values ?? new Dictionary<string, string?>(),
            ["WorkflowsRuntimeEntityFrameworkCore"], [], [],
            [new ShellFeaturePreparationDescriptor("WorkflowsRuntimeEntityFrameworkCore", [],
                typeof(RuntimeEntityFrameworkCoreFeature), false)],
            ["WorkflowsRuntimeEntityFrameworkCore"], [], []);

    private static ShellSettingsPreparationContext RuntimePairContext(IReadOnlyDictionary<string, string?> values) =>
        new(new ShellId("default"), values,
            ["WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence"], [], [],
            [
                new ShellFeaturePreparationDescriptor("WorkflowsRuntimeEntityFrameworkCore", [],
                    typeof(RuntimeEntityFrameworkCoreFeature), false),
                new ShellFeaturePreparationDescriptor("WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence", [],
                    typeof(RuntimeBookmarksEntityFrameworkCoreFeature), false)
            ],
            ["WorkflowsRuntimeEntityFrameworkCore", "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence"], [], []);
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
