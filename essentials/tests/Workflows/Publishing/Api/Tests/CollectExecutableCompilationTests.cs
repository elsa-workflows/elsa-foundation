using Elsa.Workflows.Publishing.Handlers;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class CollectExecutableCompilationTests
{
    [Fact]
    public async Task Collects_sources_in_stable_identity_order_with_tenant_and_dependency_claims()
    {
        var calls = new List<string>();
        var second = new ZSource(calls);
        var first = new ASource(calls);
        var handler = new CollectExecutableCompilation([second, first]);
        var collecting = Event(tenantId: "tenant-a");

        await handler.Handle(collecting, CancellationToken.None);

        Assert.Equal([nameof(ASource), nameof(ZSource)], calls);
        Assert.Equal("tenant-a", first.TenantId);
        Assert.Equal("tenant-a", second.TenantId);
        Assert.Equal(2, collecting.Contributions.Count);
        Assert.Single(collecting.Contributions.SelectMany(x => x.NodeMetadata));
        var dependency = Assert.Single(collecting.Contributions.SelectMany(x => x.Dependencies));
        Assert.Equal("child-artifact", dependency.ArtifactId);
        Assert.Equal("sha256:child", dependency.ArtifactHash);
        Assert.All(collecting.Contributions, contribution => Assert.False(string.IsNullOrWhiteSpace(contribution.SourceIdentity)));
    }

    [Fact]
    public async Task Equal_metadata_claims_are_idempotent()
    {
        var handler = new CollectExecutableCompilation(
        [
            new FixedSource(metadata: [new ExecutableNodeMetadataClaim("node-root", "runtime.pin", "same")]),
            new OtherFixedSource(metadata: [new ExecutableNodeMetadataClaim("node-root", "runtime.pin", "same")])
        ]);
        var collecting = Event();

        await handler.Handle(collecting, CancellationToken.None);

        Assert.Single(collecting.Contributions);
        Assert.Single(collecting.Contributions.SelectMany(x => x.NodeMetadata));
    }

    [Fact]
    public async Task Legacy_metadata_source_is_adapted_into_the_generalized_event()
    {
        var handler = new CollectExecutableCompilation(
            sources: [],
            legacyMetadataSources: [new LegacySource()]);
        var collecting = Event();

        await handler.Handle(collecting, CancellationToken.None);

        var contribution = Assert.Single(collecting.Contributions);
        var claim = Assert.Single(contribution.NodeMetadata);
        Assert.Equal("node-root", claim.ExecutableNodeId);
        Assert.Equal("runtime.pin", claim.Key);
        Assert.Equal("legacy", claim.Value);
    }

    [Fact]
    public async Task Unequal_metadata_claims_fail_with_deterministically_ordered_owners()
    {
        var first = new FixedSource(metadata: [new ExecutableNodeMetadataClaim("node-root", "runtime.pin", "one")]);
        var second = new OtherFixedSource(metadata: [new ExecutableNodeMetadataClaim("node-root", "runtime.pin", "two")]);
        var collecting = Event();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new CollectExecutableCompilation([second, first]).Handle(collecting, CancellationToken.None));

        var owners = new[] { first.GetType().FullName!, second.GetType().FullName! }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            $"Executable node 'node-root' metadata key 'runtime.pin' has unequal values from owners '{owners[0]}' and '{owners[1]}'.",
            exception.Message);
        Assert.Empty(collecting.Contributions);
    }

    [Fact]
    public async Task Conflicting_dependency_hashes_fail_before_the_event_is_mutated()
    {
        var first = new FixedSource(dependencies: [new ExecutableDependencyClaim("node-root", "child-artifact", "sha256:one")]);
        var second = new OtherFixedSource(dependencies: [new ExecutableDependencyClaim("node-root", "child-artifact", "sha256:two")]);
        var collecting = Event();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new CollectExecutableCompilation([second, first]).Handle(collecting, CancellationToken.None));

        Assert.Contains("artifact 'child-artifact' has conflicting hashes", exception.Message, StringComparison.Ordinal);
        Assert.Empty(collecting.Contributions);
    }

    [Fact]
    public async Task One_node_cannot_claim_two_child_artifacts()
    {
        var handler = new CollectExecutableCompilation(
        [
            new FixedSource(dependencies: [new ExecutableDependencyClaim("node-root", "child-a", "sha256:a")]),
            new OtherFixedSource(dependencies: [new ExecutableDependencyClaim("node-root", "child-b", "sha256:b")])
        ]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(Event(), CancellationToken.None));

        Assert.Contains("node 'node-root' has conflicting dependency claims", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unequal_claim_cannot_replace_compiled_node_metadata()
    {
        var source = new FixedSource(metadata: [new ExecutableNodeMetadataClaim("node-root", "compiled-key", "replacement")]);
        var handler = new CollectExecutableCompilation(
            [source]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(Event(root: Root(new Dictionary<string, string> { ["compiled-key"] = "original" })), CancellationToken.None));

        var owners = new[] { "compiled:node-root", source.GetType().FullName! }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            $"Executable node 'node-root' metadata key 'compiled-key' has unequal values from owners '{owners[0]}' and '{owners[1]}'.",
            exception.Message);
    }

    [Fact]
    public async Task Equal_claim_matches_compiled_node_metadata_idempotently()
    {
        var collecting = Event(root: Root(new Dictionary<string, string> { ["compiled-key"] = "same" }));

        await new CollectExecutableCompilation(
                [new FixedSource(metadata: [new ExecutableNodeMetadataClaim("node-root", "compiled-key", "same")])])
            .Handle(collecting, CancellationToken.None);

        Assert.Single(collecting.Contributions);
    }

    [Fact]
    public async Task Unknown_dependency_node_fails_closed()
    {
        var handler = new CollectExecutableCompilation(
        [
            new FixedSource(dependencies: [new ExecutableDependencyClaim("missing", "child-artifact", "sha256:child")])
        ]);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(Event(), CancellationToken.None));

        Assert.Equal("Executable dependency contribution targets unknown node 'missing'.", exception.Message);
    }

    [Theory]
    [InlineData("", "key", "value")]
    [InlineData("node-root", "", "value")]
    [InlineData("node-root", "key", "")]
    public async Task Blank_metadata_claim_parts_fail_closed(string nodeId, string key, string value)
    {
        var collecting = Event();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new CollectExecutableCompilation(
                [new FixedSource(metadata: [new ExecutableNodeMetadataClaim(nodeId, key, value)])])
            .Handle(collecting, CancellationToken.None));

        Assert.Empty(collecting.Contributions);
    }

    [Fact]
    public async Task Unknown_metadata_node_fails_closed()
    {
        var collecting = Event();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new CollectExecutableCompilation(
                [new FixedSource(metadata: [new ExecutableNodeMetadataClaim("missing", "key", "value")])])
            .Handle(collecting, CancellationToken.None));

        Assert.Equal("Executable metadata contribution targets unknown node 'missing'.", exception.Message);
        Assert.Empty(collecting.Contributions);
    }

    [Theory]
    [InlineData("", "artifact", "hash")]
    [InlineData("node-root", "", "hash")]
    [InlineData("node-root", "artifact", "")]
    public async Task Blank_dependency_claim_parts_fail_closed(string nodeId, string artifactId, string artifactHash)
    {
        var collecting = Event();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new CollectExecutableCompilation(
                [new FixedSource(dependencies: [new ExecutableDependencyClaim(nodeId, artifactId, artifactHash)])])
            .Handle(collecting, CancellationToken.None));

        Assert.Empty(collecting.Contributions);
    }

    [Fact]
    public async Task Output_is_canonical_and_exact_duplicates_are_merged()
    {
        var source = new FixedSource(
            metadata:
            [
                new ExecutableNodeMetadataClaim("node-root", "z", "2"),
                new ExecutableNodeMetadataClaim("node-root", "a", "1"),
                new ExecutableNodeMetadataClaim("node-root", "a", "1")
            ],
            dependencies:
            [
                new ExecutableDependencyClaim("node-root", "child", "hash"),
                new ExecutableDependencyClaim("node-root", "child", "hash")
            ]);
        var collecting = Event();

        await new CollectExecutableCompilation([source]).Handle(collecting, CancellationToken.None);

        var contribution = Assert.Single(collecting.Contributions);
        Assert.Equal(["a", "z"], contribution.NodeMetadata.Select(x => x.Key));
        Assert.Single(contribution.Dependencies);
    }

    [Fact]
    public async Task Same_child_identity_may_be_claimed_by_multiple_nodes()
    {
        var collecting = Event(root: Root(children: [Root(nodeId: "node-child")]));
        var source = new FixedSource(dependencies:
        [
            new ExecutableDependencyClaim("node-root", "child", "hash"),
            new ExecutableDependencyClaim("node-child", "child", "hash")
        ]);

        await new CollectExecutableCompilation([source]).Handle(collecting, CancellationToken.None);

        Assert.Equal(2, Assert.Single(collecting.Contributions).Dependencies.Count);
    }

    [Fact]
    public async Task Null_source_result_fails_with_source_identity()
    {
        var source = new NullSource();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new CollectExecutableCompilation([source]).Handle(Event(), CancellationToken.None));

        Assert.Equal($"Executable compilation source '{source.GetType().FullName}' returned null.", exception.Message);
    }

    [Fact]
    public async Task Source_exception_leaves_event_unmodified()
    {
        var collecting = Event();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CollectExecutableCompilation([new ThrowingSource()]).Handle(collecting, CancellationToken.None));

        Assert.Empty(collecting.Contributions);
    }

    [Fact]
    public void Duplicate_source_identities_fail_at_composition()
    {
        var exception = Assert.Throws<ArgumentException>(() => new CollectExecutableCompilation(
            [new FixedSource(), new FixedSource()]));

        Assert.Contains(typeof(FixedSource).FullName!, exception.Message, StringComparison.Ordinal);
    }

    private static ExecutableCompilationCollecting Event(string? tenantId = null, ExecutableNode? root = null) =>
        new(new ExecutableCompilationContext(
            new WorkflowExecutableCompileRequest(
                VersionId: "version-1",
                Scope: WorkflowExecutableReferenceScope.Published,
                CreatedAt: DateTimeOffset.UnixEpoch,
                PublishedAt: DateTimeOffset.UnixEpoch,
                ExpiresAt: null,
                ArtifactIdPrefix: "artifact-",
                TenantId: tenantId),
            new WorkflowExecutableCompileSource(
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: "1.0.0",
                State: null!,
                SourceKind: "WorkflowDefinitionVersion",
                SourceId: "version-1",
                SourceVersion: "1.0.0"),
            root ?? Root()));

    private static ExecutableNode Root(
        IReadOnlyDictionary<string, string>? metadata = null,
        string nodeId = "node-root",
        IReadOnlyCollection<ExecutableNode>? children = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"activity-{nodeId}",
            activityType: "Test.Root",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(
                "Test",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: metadata ?? new Dictionary<string, string>(),
            childSlots: children is null ? null : [new ExecutableChildSlot("Body", children)]);

    private sealed class ASource(List<string> calls) : IExecutableCompilationSource
    {
        public string? TenantId { get; private set; }

        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default)
        {
            calls.Add(nameof(ASource));
            TenantId = context.TenantId;
            return ValueTask.FromResult(new ExecutableCompilationContribution(
                nodeMetadata: [new ExecutableNodeMetadataClaim("node-root", "runtime.pin", "value")]));
        }
    }

    private sealed class ZSource(List<string> calls) : IExecutableCompilationSource
    {
        public string? TenantId { get; private set; }

        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default)
        {
            calls.Add(nameof(ZSource));
            TenantId = context.TenantId;
            return ValueTask.FromResult(new ExecutableCompilationContribution(
                dependencies: [new ExecutableDependencyClaim("node-root", "child-artifact", "sha256:child")]));
        }
    }

    private class FixedSource(
        IReadOnlyCollection<ExecutableNodeMetadataClaim>? metadata = null,
        IReadOnlyCollection<ExecutableDependencyClaim>? dependencies = null) : IExecutableCompilationSource
    {
        public virtual ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutableCompilationContribution(metadata, dependencies));
    }

    private sealed class OtherFixedSource(
        IReadOnlyCollection<ExecutableNodeMetadataClaim>? metadata = null,
        IReadOnlyCollection<ExecutableDependencyClaim>? dependencies = null) : FixedSource(metadata, dependencies);

    private sealed class NullSource : IExecutableCompilationSource
    {
        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ExecutableCompilationContribution>(null!);
    }

    private sealed class ThrowingSource : IExecutableCompilationSource
    {
        public ValueTask<ExecutableCompilationContribution> GetContributionAsync(
            ExecutableCompilationContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class LegacySource : IExecutableNodeMetadataSource
    {
        public ValueTask<IReadOnlyCollection<ExecutableNodeMetadataContribution>> GetMetadataAsync(
            ExecutableNodeMetadataContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ExecutableNodeMetadataContribution>>(
                [new ExecutableNodeMetadataContribution("node-root", "runtime.pin", "legacy")]);
    }
}
