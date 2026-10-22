using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Workflows.Design.Services;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.Equal(artifact.ArtifactId, EtlNativeBoundary.ReadPin(claim.Value, selection.ToToken()).ArtifactId);
    }

    [Fact]
    public async Task Compilation_source_pins_v2_artifact_under_the_existing_key_and_native_reader_validates_it()
    {
        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 7, EtlV2TestPipelines.Create()).Artifact);
        var selection = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash);
        var expectedScope = new EtlScope("tenant-a", "partition-a");
        var store = new RecordingDefinitionStore(expectedScope, artifact);
        var source = new EtlPublishedArtifactPinSource(
            store,
            new EtlArtifactCompiler(),
            new FixedPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("partition-a"))),
            new EtlArtifactCompiler());

        var contribution = await source.GetContributionAsync(Context(RunEtlNode(selection.ToToken()), tenantId: "tenant-a"));

        var claim = Assert.Single(contribution.NodeMetadata);
        Assert.Equal("etl.pipeline.v1", claim.Key);
        Assert.Equal(1, EtlNativeBoundary.FormatVersion);
        var embedded = EtlNativeBoundary.ReadPin(claim.Value, selection.ToToken());
        Assert.Equal(EtlArtifactV2Json.FormatVersion, embedded.FormatVersion);
        Assert.Equal(artifact.ArtifactId, embedded.ArtifactId);
        Assert.Equal(artifact.ContentHash, embedded.ContentHash);
        Assert.Equal(artifact.Pipeline.GetRawText(), embedded.Pipeline.GetRawText());
        Assert.False(embedded.Pipeline.GetProperty("http").TryGetProperty("maxRetainedMetadataBytes", out _));
        Assert.Equal(embedded.Pipeline.GetRawText(), EtlArtifactV2Json.Write(EtlArtifactV2Json.Read(embedded.Pipeline)).GetRawText());
    }

    [Fact]
    public async Task Compilation_source_and_native_reader_preserve_default_v2_artifact_hash_and_bytes_at_the_artifact_limit()
    {
        var artifact = CompileAtEnvelopeLimit();
        Assert.Equal(EtlArtifactV2Json.Read(artifact.Pipeline).Limits.MaxArtifactBytes,
            JsonSerializer.SerializeToUtf8Bytes(artifact).Length);
        var selection = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash);
        var expectedScope = new EtlScope("tenant-a", "partition-a");
        var store = new RecordingDefinitionStore(expectedScope, artifact);
        var source = new EtlPublishedArtifactPinSource(
            store,
            new EtlArtifactCompiler(),
            new FixedPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("partition-a"))),
            new EtlArtifactCompiler());

        var contribution = await source.GetContributionAsync(Context(RunEtlNode(selection.ToToken()), tenantId: "tenant-a"));
        var claim = Assert.Single(contribution.NodeMetadata);
        var restored = EtlNativeBoundary.ReadPin(claim.Value, selection.ToToken());

        Assert.Equal(artifact.ContentHash, restored.ContentHash);
        Assert.Equal(artifact.ArtifactId, restored.ArtifactId);
        Assert.Equal(artifact.Pipeline.GetRawText(), restored.Pipeline.GetRawText());
        Assert.False(restored.Pipeline.GetProperty("http").TryGetProperty("maxRetainedMetadataBytes", out _));
        Assert.Equal(64L * 1024 * 1024, EtlArtifactV2Json.Read(restored.Pipeline).Http.MaxRetainedMetadataBytes);
    }

    [Fact]
    public void Native_pin_reader_rejects_wrong_version_hash_and_noncanonical_v2_documents()
    {
        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 7, EtlV2TestPipelines.Create()).Artifact);
        var selection = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash).ToToken();

        var wrongVersion = new EtlArtifact(3, artifact.ArtifactId, artifact.ContentHash,
            artifact.DefinitionId, artifact.DefinitionVersion, artifact.Pipeline);
        Assert.Throws<InvalidDataException>(() => EtlNativeBoundary.ReadPin(SerializeArtifact(wrongVersion), selection));

        var changedPipelineJson = artifact.Pipeline.GetRawText().Replace("\"recordsPointer\":\"/records\"", "\"recordsPointer\":\"/items\"", StringComparison.Ordinal);
        Assert.NotEqual(artifact.Pipeline.GetRawText(), changedPipelineJson);
        using var changedPipelineDocument = JsonDocument.Parse(changedPipelineJson);
        var wrongHash = new EtlArtifact(artifact.FormatVersion, artifact.ArtifactId, artifact.ContentHash,
            artifact.DefinitionId, artifact.DefinitionVersion, changedPipelineDocument.RootElement);
        Assert.Throws<InvalidDataException>(() => EtlNativeBoundary.ReadPin(SerializeArtifact(wrongHash), selection));

        using var noncanonicalDocument = JsonDocument.Parse(ReverseObjectProperties(artifact.Pipeline));
        var noncanonical = new EtlArtifact(artifact.FormatVersion, artifact.ArtifactId, artifact.ContentHash,
            artifact.DefinitionId, artifact.DefinitionVersion, noncanonicalDocument.RootElement);
        var noncanonicalPin = SerializeArtifact(noncanonical);
        using var serializedPin = JsonDocument.Parse(noncanonicalPin);
        Assert.NotEqual(artifact.Pipeline.GetRawText(), serializedPin.RootElement.GetProperty("pipeline").GetRawText());
        Assert.Throws<InvalidDataException>(() => EtlNativeBoundary.ReadPin(noncanonicalPin, selection));
    }

    [Fact]
    public void Native_pin_reader_rejects_default_shape_with_missing_required_property_or_other_noncanonical_bytes()
    {
        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 7, EtlV2TestPipelines.Create()).Artifact);

        var missingSourceNode = JsonNode.Parse(artifact.Pipeline.GetRawText())!;
        missingSourceNode.AsObject().Remove("source");
        using var missingSourceDocument = JsonDocument.Parse(missingSourceNode.ToJsonString());
        var missingSourceHash = EtlArtifactJson.Hash(missingSourceDocument.RootElement);
        var missingSource = new EtlArtifact(artifact.FormatVersion,
            EtlArtifactJson.CreateArtifactId(artifact.FormatVersion, artifact.DefinitionId, artifact.DefinitionVersion, missingSourceHash),
            missingSourceHash, artifact.DefinitionId, artifact.DefinitionVersion, missingSourceDocument.RootElement);
        var missingSourceSelection = new EtlPublishedSelection(missingSource.DefinitionId, missingSource.DefinitionVersion, missingSource.ContentHash).ToToken();
        Assert.Throws<InvalidDataException>(() => EtlNativeBoundary.ReadPin(SerializeArtifact(missingSource), missingSourceSelection));

        using var reorderedDocument = JsonDocument.Parse(ReverseObjectProperties(artifact.Pipeline));
        var reorderedHash = EtlArtifactJson.Hash(reorderedDocument.RootElement);
        var reordered = new EtlArtifact(artifact.FormatVersion,
            EtlArtifactJson.CreateArtifactId(artifact.FormatVersion, artifact.DefinitionId, artifact.DefinitionVersion, reorderedHash),
            reorderedHash, artifact.DefinitionId, artifact.DefinitionVersion, reorderedDocument.RootElement);
        var reorderedSelection = new EtlPublishedSelection(reordered.DefinitionId, reordered.DefinitionVersion, reordered.ContentHash).ToToken();
        Assert.Throws<InvalidDataException>(() => EtlNativeBoundary.ReadPin(SerializeArtifact(reordered), reorderedSelection));
    }

    [Fact]
    public async Task V1_only_compiler_composition_refuses_v2_pinning_with_a_safe_explicit_error()
    {
        var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 7, EtlV2TestPipelines.Create()).Artifact);
        var selection = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash);
        var store = new RecordingDefinitionStore(new EtlScope("tenant-a", "partition-a"), artifact);
        var services = new ServiceCollection();
        services.AddSingleton<IEtlDefinitionStore>(store);
        services.AddSingleton<IEtlArtifactCompiler>(new EtlArtifactCompiler());
        services.AddSingleton<IPersistenceAccessContextAccessor>(
            new FixedPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("partition-a"))));
        new Elsa.Etl.Workflows.Design.EtlWorkflowsDesignFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var source = Assert.IsType<EtlPublishedArtifactPinSource>(Assert.Single(scope.ServiceProvider.GetServices<IExecutableCompilationSource>()));

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.GetContributionAsync(Context(RunEtlNode(selection.ToToken()), tenantId: "tenant-a")));

        Assert.Contains("v2", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", error.Message, StringComparison.OrdinalIgnoreCase);
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

    private static string SerializeArtifact(EtlArtifact artifact) =>
        JsonSerializer.Serialize(artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static EtlArtifact CompileAtEnvelopeLimit()
    {
        var compiler = new EtlArtifactCompiler();
        var pipeline = EtlV2TestPipelines.Create();
        var artifactLimit = EtlArtifact.MaximumByteLength;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            pipeline = pipeline with { Limits = pipeline.Limits with { MaxArtifactBytes = artifactLimit } };
            var result = compiler.Compile("orders", 7, pipeline);
            Assert.Empty(result.Diagnostics);
            var artifact = Assert.IsType<EtlArtifact>(result.Artifact);
            var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(artifact).Length;
            if (envelopeBytes == artifactLimit)
                return artifact;
            artifactLimit = envelopeBytes;
        }

        throw new InvalidOperationException("Could not align the v2 artifact limit with its serialized envelope size.");
    }

    private static string ReverseObjectProperties(JsonElement document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.EnumerateObject().Reverse())
            {
                writer.WritePropertyName(property.Name);
                property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
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
