using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Core.Manifests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>
/// Covers the seam that closes #1172: the host exports which lane belongs to which target, and the schema
/// tool applies only that target's share instead of the host-wide union.
/// <para>
/// The assertion that matters throughout is not that the descriptor round-trips, but that a target's
/// manifest is missing the other target's storage units. Over-provisioning is inert, so a test that only
/// checks the target's own units present would pass against the bug it exists to catch.
/// </para>
/// </summary>
public sealed class GroundworkTargetDeploymentDescriptorTests : IDisposable
{
    private static readonly Type WorkflowsDesign = typeof(WorkflowsDesignGroundworkStorageManifestSource);
    private static readonly Type ActivitiesDesign = typeof(ActivitiesDesignGroundworkStorageManifestSource);
    private const string DesignTarget = "design";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"elsa-gw-descriptor-{Guid.NewGuid():N}");

    public GroundworkTargetDeploymentDescriptorTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void An_unsplit_host_describes_one_default_target_carrying_every_lane()
    {
        var source = new GroundworkAllFeaturesDeploymentSchema();

        var descriptor = GroundworkTargetDeploymentDescriptorFactory.Create(source);

        var target = Assert.Single(descriptor.Targets);
        Assert.Equal(GroundworkTargetNames.Default, target.Name);

        // The bare identity is what every database admitted before targets existed was applied under.
        Assert.Equal("elsa-documents", target.ManifestIdentity);
        Assert.Contains(GroundworkTargetDeploymentDescriptor.NameOf(WorkflowsDesign), target.ManifestSources);
    }

    [Fact]
    public void A_split_host_gives_each_target_only_the_lanes_bound_to_it()
    {
        var descriptor = CreateSplitDescriptor();

        var design = descriptor.RequireTarget(DesignTarget);
        var @default = descriptor.RequireTarget(GroundworkTargetNames.Default);

        Assert.Equal(
            [
                GroundworkTargetDeploymentDescriptor.NameOf(ActivitiesDesign),
                GroundworkTargetDeploymentDescriptor.NameOf(WorkflowsDesign)
            ],
            design.ManifestSources.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            GroundworkTargetDeploymentDescriptor.NameOf(WorkflowsDesign),
            @default.ManifestSources);

        // A named target derives its own identity, so two targets pointed at one database do not contend
        // for a single Groundwork schema-state row.
        Assert.Equal($"elsa-documents.{DesignTarget}", design.ManifestIdentity);
        Assert.Equal("elsa-documents", @default.ManifestIdentity);
    }

    [Fact]
    public void The_design_target_applies_no_runtime_storage_units()
    {
        var path = WriteSplitDescriptor();
        var wholeHost = new GroundworkAllFeaturesDeploymentSchema().CreateManifest();

        var design = new GroundworkTargetDeploymentSchema(path, DesignTarget).CreateManifest();
        var @default = new GroundworkTargetDeploymentSchema(path, GroundworkTargetNames.Default).CreateManifest();

        var designUnits = UnitsOf(design);
        var defaultUnits = UnitsOf(@default);
        var wholeHostUnits = UnitsOf(wholeHost);

        // The point of the unit: the design database stops carrying the runtime lane's tables.
        Assert.NotEmpty(designUnits);
        Assert.Empty(designUnits.Intersect(defaultUnits, StringComparer.Ordinal));
        Assert.True(designUnits.Count < wholeHostUnits.Count);

        // And nothing is lost: the two targets together still account for the whole host.
        Assert.Equal(wholeHostUnits, designUnits.Union(defaultUnits, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_target_manifest_identity_follows_the_target_rather_than_the_host()
    {
        var path = WriteSplitDescriptor();

        Assert.Equal(
            $"elsa-documents.{DesignTarget}",
            new GroundworkTargetDeploymentSchema(path, DesignTarget).CreateManifest().Identity.Value);
        Assert.Equal(
            "elsa-documents",
            new GroundworkTargetDeploymentSchema(path, GroundworkTargetNames.Default).CreateManifest().Identity.Value);
    }

    [Fact]
    public void An_absent_descriptor_refuses_rather_than_applying_the_host_wide_union()
    {
        var source = new GroundworkTargetDeploymentSchema(descriptorPath: null, DesignTarget);

        var exception = Assert.Throws<GroundworkTargetDeploymentDescriptorException>(() => source.CreateManifest());
        Assert.Contains(GroundworkTargetDeploymentSchema.DescriptorOption, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_options_the_tool_forwards_select_the_target()
    {
        // The path the tool actually takes: activate parameterlessly, then hand over the operator's options.
        var path = WriteSplitDescriptor();
        var source = new GroundworkTargetDeploymentSchema();

        source.Configure(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GroundworkTargetDeploymentSchema.DescriptorOption] = path,
            [GroundworkTargetDeploymentSchema.TargetOption] = DesignTarget
        });

        Assert.Equal($"elsa-documents.{DesignTarget}", source.CreateManifest().Identity.Value);
        Assert.Equal(DesignTarget, source.TargetName);
    }

    [Fact]
    public void An_option_this_source_does_not_understand_refuses_rather_than_being_ignored()
    {
        var source = new GroundworkTargetDeploymentSchema();

        var exception = Assert.Throws<GroundworkTargetDeploymentDescriptorException>(() => source.Configure(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [GroundworkTargetDeploymentSchema.DescriptorOption] = WriteSplitDescriptor(),
                ["lane"] = "design"
            }));

        // A misspelled or obsolete option would otherwise apply the default target's schema silently.
        Assert.Contains("lane", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuring_without_a_target_option_means_the_default_target()
    {
        var path = WriteSplitDescriptor();
        var source = new GroundworkTargetDeploymentSchema();

        source.Configure(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GroundworkTargetDeploymentSchema.DescriptorOption] = path
        });

        // An unsplit deployment names no target, and must keep applying under the bare identity.
        Assert.Equal("elsa-documents", source.CreateManifest().Identity.Value);
    }

    [Fact]
    public void A_descriptor_file_that_is_not_there_refuses()
    {
        var missing = Path.Combine(directory, "absent.json");

        Assert.Throws<GroundworkTargetDeploymentDescriptorException>(
            () => new GroundworkTargetDeploymentSchema(missing, DesignTarget).CreateManifest());
    }

    [Fact]
    public void A_target_the_host_never_declared_refuses_and_names_the_ones_it_did()
    {
        var path = WriteSplitDescriptor();

        var exception = Assert.Throws<GroundworkTargetDeploymentDescriptorException>(
            () => new GroundworkTargetDeploymentSchema(path, "reporting").CreateManifest());

        Assert.Contains("reporting", exception.Message, StringComparison.Ordinal);
        Assert.Contains(DesignTarget, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_descriptor_whose_recorded_identity_disagrees_with_this_build_refuses()
    {
        // The identity decides which schema-state row is written, so a descriptor carrying someone else's
        // is describing another deployment even when every lane happens to line up.
        var path = WriteDescriptor(GroundworkTargetDeploymentDescriptor.Create(
            GroundworkTargetDeploymentDescriptor.NameOf(typeof(GroundworkAllFeaturesDeploymentSchema)),
            CreateSplitDescriptor().Targets
                .Select(entry => entry.Name == DesignTarget
                    ? GroundworkTargetDeploymentEntry.Create(entry.Name, "elsa-documents.other", entry.ManifestSources)
                    : entry)
                .ToArray()));

        var exception = Assert.Throws<GroundworkTargetDeploymentDescriptorException>(
            () => new GroundworkTargetDeploymentSchema(path, DesignTarget).CreateManifest());

        Assert.Contains("elsa-documents.other", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"elsa-documents.{DesignTarget}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_descriptor_that_no_longer_accounts_for_every_lane_refuses_as_stale()
    {
        // Freshness has to be enforceable rather than conventional: dropping a lane is what a descriptor
        // exported before a feature was added looks like.
        var split = CreateSplitDescriptor();
        var path = WriteDescriptor(GroundworkTargetDeploymentDescriptor.Create(
            GroundworkTargetDeploymentDescriptor.NameOf(typeof(GroundworkAllFeaturesDeploymentSchema)),
            split.Targets
                .Select(entry => entry.Name == DesignTarget
                    ? GroundworkTargetDeploymentEntry.Create(
                        entry.Name,
                        entry.ManifestIdentity,
                        entry.ManifestSources
                            .Where(item => item != GroundworkTargetDeploymentDescriptor.NameOf(WorkflowsDesign))
                            .ToArray())
                    : entry)
                .ToArray()));

        var exception = Assert.Throws<GroundworkTargetDeploymentDescriptorException>(
            () => new GroundworkTargetDeploymentSchema(path, DesignTarget).CreateManifest());

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WorkflowsDesign.FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_descriptor_from_a_newer_format_refuses_instead_of_guessing()
    {
        var path = Path.Combine(directory, "descriptor.json");
        File.WriteAllText(path, CreateSplitDescriptor().ToJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"));

        Assert.Throws<GroundworkTargetDeploymentDescriptorException>(
            () => new GroundworkTargetDeploymentSchema(path, DesignTarget).CreateManifest());
    }

    [Fact]
    public void A_descriptor_survives_a_round_trip_through_json()
    {
        var descriptor = CreateSplitDescriptor();

        var restored = GroundworkTargetDeploymentDescriptor.FromJson(descriptor.ToJson());

        Assert.Equal(descriptor.DeploymentSchemaSource, restored.DeploymentSchemaSource);
        Assert.Equal(
            descriptor.Targets.Select(entry => (entry.Name, entry.ManifestIdentity)),
            restored.Targets.Select(entry => (entry.Name, entry.ManifestIdentity)));
        Assert.Equal(
            descriptor.RequireTarget(DesignTarget).ManifestSources,
            restored.RequireTarget(DesignTarget).ManifestSources);
    }

    [Fact]
    public async Task A_composed_split_host_exports_the_bindings_its_features_actually_made()
    {
        // The hand-built descriptors above prove the narrowing; this proves the export is wired to real
        // feature composition, which is the only place the bindings are true.
        var services = new ServiceCollection();
        new SqliteGroundworkProviderFeature().ConfigureServices(services);
        new GroundworkTargetsFeature
        {
            Targets = new Dictionary<string, GroundworkTargetEntry>
            {
                [GroundworkTargetNames.Default] = new()
                {
                    Provider = SqliteGroundworkDocumentStoreRegistration.ProviderIdentity,
                    ConnectionString = "Data Source=elsa-runtime.db"
                },
                [DesignTarget] = new()
                {
                    Provider = SqliteGroundworkDocumentStoreRegistration.ProviderIdentity,
                    ConnectionString = "Data Source=elsa-design.db"
                }
            }
        }.ConfigureServices(services);
        new WorkflowsDesignGroundworkPersistenceFeature { Target = DesignTarget }.ConfigureServices(services);
        new ActivitiesDesignGroundworkPersistenceFeature { Target = DesignTarget }.ConfigureServices(services);
        services.AddGroundworkStorageComposition<GroundworkAllFeaturesDeploymentSchema>();

        await using var provider = services.BuildServiceProvider();
        var descriptor = provider.CreateGroundworkDeploymentDescriptor();

        // The design features place three sources, not two: the atomic-write ledger travels with the lane
        // whose commits it records, which is what makes a design-only database self-contained.
        var design = descriptor.RequireTarget(DesignTarget);
        Assert.Equal(
            [
                GroundworkTargetDeploymentDescriptor.NameOf(ActivitiesDesign),
                GroundworkTargetDeploymentDescriptor.NameOf(typeof(GroundworkDesignAtomicWriteStorageManifestSource)),
                GroundworkTargetDeploymentDescriptor.NameOf(WorkflowsDesign)
            ],
            design.ManifestSources.Order(StringComparer.Ordinal));

        // A lane this host never placed stays on the default target, which is what keeps an unsplit
        // deployment reading exactly as it did before targets existed.
        Assert.DoesNotContain(
            GroundworkTargetDeploymentDescriptor.NameOf(WorkflowsDesign),
            descriptor.RequireTarget(GroundworkTargetNames.Default).ManifestSources);
    }

    private static GroundworkTargetDeploymentDescriptor CreateSplitDescriptor()
    {
        var bindings = new GroundworkManifestBindings();
        bindings.Bind(WorkflowsDesign, DesignTarget);
        bindings.Bind(ActivitiesDesign, DesignTarget);
        return GroundworkTargetDeploymentDescriptorFactory.Create(new GroundworkAllFeaturesDeploymentSchema(), bindings);
    }

    private string WriteSplitDescriptor() => WriteDescriptor(CreateSplitDescriptor());

    private string WriteDescriptor(GroundworkTargetDeploymentDescriptor descriptor)
    {
        var path = Path.Combine(directory, $"descriptor-{Guid.NewGuid():N}.json");
        descriptor.WriteTo(path);
        return path;
    }

    private static IReadOnlyList<string> UnitsOf(StorageManifest manifest) => manifest
        .StorageUnits
        .Select(unit => unit.Identity.Value)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
