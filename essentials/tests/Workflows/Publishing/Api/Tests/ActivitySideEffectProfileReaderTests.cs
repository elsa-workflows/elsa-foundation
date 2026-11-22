using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Spec 261 FR-513 (T512): the design-time side-effect profile reader gives an activity version the profile the
/// executable compiler would pin, by the same attribute rule: a ReplaySafe CLR activity, an unmarked one (External), a
/// reusable graph activity (External), and an unknown version or a CLR type that is not loaded (no profile).
/// </summary>
public sealed class ActivitySideEffectProfileReaderTests
{
    private static readonly ActivityDefinitionVersion ReplaySafeVersion = ClrVersion("activity-replay-safe", typeof(ReplaySafeLeafActivity));
    private static readonly ActivityDefinitionVersion UnmarkedVersion = ClrVersion("activity-unmarked", typeof(UnmarkedLeafActivity));
    private static readonly ActivityDefinitionVersion SequenceVersion = ClrVersion("activity-sequence", typeof(Elsa.Activities.Sequence.Activities.Sequence));
    private static readonly ActivityDefinitionVersion GhostVersion = ClrVersion("activity-ghost", alias: "Test.NotLoaded");
    private static readonly ActivityDefinitionVersion GraphVersion = new("1.0.0", "activity-graph-definition")
    {
        Id = "activity-graph",
        ProviderKey = WellKnownRuntimeActivityConsumers.GraphActivity,
        ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
        ConsumerKey = WellKnownRuntimeActivityConsumers.GraphActivity,
        ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
        DescriptorPayload = JsonSerializer.SerializeToElement(new { type = "graph" }),
        Inputs = []
    };

    [Theory]
    [InlineData("activity-replay-safe", SideEffectProfile.ReplaySafe)]
    [InlineData("activity-unmarked", SideEffectProfile.External)]
    [InlineData("activity-graph", SideEffectProfile.External)]
    [InlineData("activity-sequence", SideEffectProfile.ReplaySafe)]
    public async Task A_resolvable_version_gets_the_profile_the_compiler_pins(string activityVersionId, SideEffectProfile expected)
    {
        Assert.Equal(expected, await NewReader().ReadAsync(activityVersionId));
    }

    [Theory]
    [InlineData("activity-missing")]
    [InlineData("activity-ghost")]
    [InlineData("")]
    public async Task An_unknown_version_or_an_unloaded_type_has_no_profile(string activityVersionId)
    {
        Assert.Null(await NewReader().ReadAsync(activityVersionId));
    }

    [Fact]
    public void The_publishing_feature_registers_the_reader()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IActivitySideEffectProfileReader) &&
            descriptor.ImplementationType == typeof(ActivitySideEffectProfileReader));
    }

    private static ActivitySideEffectProfileReader NewReader()
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(ReplaySafeLeafActivity), TypeAliasConvention.CanonicalAlias(typeof(ReplaySafeLeafActivity)));
        registry.RegisterType(typeof(UnmarkedLeafActivity), TypeAliasConvention.CanonicalAlias(typeof(UnmarkedLeafActivity)));
        return new ActivitySideEffectProfileReader(new VersionStore([ReplaySafeVersion, UnmarkedVersion, SequenceVersion, GhostVersion, GraphVersion]), registry);
    }

    private static ActivityDefinitionVersion ClrVersion(string id, Type? activityType = null, string? alias = null) =>
        new("1.0.0", $"{id}-definition")
        {
            Id = id,
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias ?? TypeAliasConvention.CanonicalAlias(activityType!))),
            Inputs = []
        };

    [ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
    private sealed class ReplaySafeLeafActivity : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    private sealed class UnmarkedLeafActivity : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
    }

    // Throws for a missing version, as the persistence stores do.
    private sealed class VersionStore(IReadOnlyList<ActivityDefinitionVersion> items) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.FirstOrDefault(item => item.Id == versionId) ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId));

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            GetAsync(versionId, cancellationToken);

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
