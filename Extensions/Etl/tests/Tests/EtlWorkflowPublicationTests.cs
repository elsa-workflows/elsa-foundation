using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Workflows.Design.Services;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Etl.Tests;

public sealed class EtlWorkflowPublicationTests
{
    [Fact]
    public void Selection_token_is_canonical_and_round_trips_the_full_artifact_identity()
    {
        var selection = new EtlPublishedSelection("orders.v2", 17, new string('a', 64));

        var token = selection.ToToken();

        Assert.Equal("etl1:orders.v2:17:" + new string('a', 64), token);
        Assert.Equal(selection, EtlPublishedSelection.Parse(token));
    }

    [Theory]
    [InlineData("etl1:orders:01:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("etl1:orders:1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("etl1:orders:1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("etl1:bad:id:1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Selection_token_rejects_noncanonical_or_invalid_values(string token)
    {
        Assert.Throws<FormatException>(() => EtlPublishedSelection.Parse(token));
    }

    [Fact]
    public void Selection_literal_hashes_into_the_parent_executable_identity()
    {
        var hasher = new WorkflowExecutableHasher();
        var firstToken = new EtlPublishedSelection("orders", 1, new string('a', 64)).ToToken();
        var changedToken = new EtlPublishedSelection("orders", 1, new string('b', 64)).ToToken();

        var firstHash = hasher.ComputeHash(RunEtlNode(firstToken));

        Assert.Equal(firstHash, hasher.ComputeHash(RunEtlNode(firstToken)));
        Assert.NotEqual(firstHash, hasher.ComputeHash(RunEtlNode(changedToken)));
    }

    [Fact]
    public async Task Compilation_source_embeds_the_exact_scoped_published_artifact_without_workflow_dependency_edges()
    {
        var pipeline = EtlTestPipelines.Create();
        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 7, pipeline).Artifact);
        var selection = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash);
        var expectedScope = new EtlScope("tenant-a", "partition-a");
        var store = new RecordingDefinitionStore(expectedScope, artifact);
        var source = new EtlPublishedArtifactPinSource(
            store,
            new EtlArtifactCompiler(),
            new FixedPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("partition-a"))));

        var contribution = await source.GetContributionAsync(Context(RunEtlNode(selection.ToToken()), tenantId: "tenant-a"));

        Assert.Equal(expectedScope, store.RequestedScope);
        Assert.Equal("orders", store.RequestedDefinitionId);
        Assert.Equal(7, store.RequestedDefinitionVersion);
        Assert.Empty(contribution.Dependencies);
        var claim = Assert.Single(contribution.NodeMetadata);
        Assert.Equal("etl-node", claim.ExecutableNodeId);
        Assert.Equal("etl.pipeline.v1", claim.Key);
        Assert.True(Encoding.UTF8.GetByteCount(claim.Value) <= EtlArtifact.MaximumByteLength);
        var embedded = JsonSerializer.Deserialize<EtlArtifact>(claim.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(embedded);
        Assert.Equal(artifact.ArtifactId, embedded!.ArtifactId);
        Assert.Equal(artifact.ContentHash, embedded.ContentHash);
        Assert.Equal(artifact.Pipeline.GetRawText(), embedded.Pipeline.GetRawText());
    }

    [Fact]
    public async Task Compilation_source_rejects_a_published_version_whose_hash_differs_from_the_hashed_selection()
    {
        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 7, EtlTestPipelines.Create()).Artifact);
        var wrongSelection = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, new string('b', 64));
        var store = new RecordingDefinitionStore(new EtlScope("tenant-a", "partition-a"), artifact);
        var source = new EtlPublishedArtifactPinSource(
            store,
            new EtlArtifactCompiler(),
            new FixedPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("partition-a"))));

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.GetContributionAsync(Context(RunEtlNode(wrongSelection.ToToken()), tenantId: "tenant-a")));

        Assert.Contains("published ETL artifact", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(wrongSelection.ContentHash, error.Message, StringComparison.Ordinal);
        Assert.Equal("orders", store.RequestedDefinitionId);
        Assert.Equal(7, store.RequestedDefinitionVersion);
    }

    [Fact]
    public async Task Compilation_source_refuses_dynamic_selection_before_reading_the_design_store()
    {
        var store = new RecordingDefinitionStore(new EtlScope("tenant-a", "partition-a"), null);
        var source = new EtlPublishedArtifactPinSource(
            store,
            new EtlArtifactCompiler(),
            new FixedPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("partition-a"))));

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.GetContributionAsync(Context(RunEtlNode("dynamic", RuntimeInputBindingSource.Expression), tenantId: "tenant-a")));

        Assert.Contains("literal", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.FindPublishedCalls);
    }

    private static ExecutableCompilationContext Context(ExecutableNode node, string? tenantId = null)
    {
        var request = new WorkflowExecutableCompileRequest(
            "parent-version",
            WorkflowExecutableReferenceScope.Published,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            "artifact-parent-",
            TenantId: tenantId);
        var source = new WorkflowExecutableCompileSource(
            "parent-definition",
            "parent-version",
            "1.0.0",
            WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion",
            "parent-version",
            "1.0.0");
        return new ExecutableCompilationContext(request, source, node);
    }

    private static ExecutableNode RunEtlNode(string selection, RuntimeInputBindingSource source = RuntimeInputBindingSource.Literal)
    {
        var type = new ValueTypeDescriptor("String");
        var binding = source == RuntimeInputBindingSource.Literal
            ? new RuntimeInputBinding(
                nameof(RunEtl.Pipeline),
                type,
                ValueProtectionPolicy.InstanceInline,
                source,
                literal: ValueEnvelope.Inline(
                    type,
                    JsonSerializer.SerializeToElement(selection),
                    ValueProtectionPolicy.InstanceInline))
            : new RuntimeInputBinding(
                nameof(RunEtl.Pipeline),
                type,
                ValueProtectionPolicy.InstanceInline,
                source,
                expression: new RuntimeExpressionBinding("JavaScript", selection));

        return new ExecutableNode(
            "etl-node",
            "etl-node",
            RunEtl.ActivityType,
            "1.0.0",
            new RuntimeActivityDescriptor(
                "test.clr-activity",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { type = RunEtl.ActivityType })),
            new Dictionary<string, RuntimeInputBinding> { [nameof(RunEtl.Pipeline)] = binding },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
    }

    private sealed class FixedPersistenceAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class RecordingDefinitionStore(EtlScope expectedScope, EtlArtifact? artifact) : IEtlDefinitionStore
    {
        public int FindPublishedCalls { get; private set; }
        public EtlScope? RequestedScope { get; private set; }
        public string? RequestedDefinitionId { get; private set; }
        public int RequestedDefinitionVersion { get; private set; }

        public ValueTask<EtlDefinitionDraft?> FindDraftAsync(EtlScope scope, string definitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EtlDefinitionWriteResult> CreateDraftAsync(EtlScope scope, string definitionId, EtlPipeline pipeline, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EtlDefinitionWriteResult> UpdateDraftAsync(EtlScope scope, string definitionId, long expectedRevision, EtlPipeline pipeline, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<EtlArtifact?> FindPublishedAsync(EtlScope scope, string definitionId, int definitionVersion, CancellationToken cancellationToken = default)
        {
            FindPublishedCalls++;
            RequestedScope = scope;
            RequestedDefinitionId = definitionId;
            RequestedDefinitionVersion = definitionVersion;
            return ValueTask.FromResult(artifact is not null && scope == expectedScope &&
                                        definitionId == artifact.DefinitionId && definitionVersion == artifact.DefinitionVersion
                ? artifact
                : null);
        }

        public ValueTask<EtlPublicationResult> PublishAsync(EtlScope scope, string definitionId, long expectedRevision, EtlArtifact publishedArtifact, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
