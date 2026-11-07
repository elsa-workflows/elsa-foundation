using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task Published_authored_source_retains_version_and_presentation_without_changing_material_identity()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var caller = new BpmnModelCaller("author", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var raw = await runtime.PublishAsync(definition, "same-operation", caller);
        var rawSource = (await sources.FindAsync(raw.SourceReferenceId))!;
        var source = PublicationSource();
        var published = await runtime.PublishFromSourceAsync(definition, source, "same-operation", caller);
        Assert.Equal(raw.Executable, published.Executable);
        Assert.Equal(raw.IncarnationId, published.IncarnationId);
        Assert.NotEqual(raw.SourceReferenceId, published.SourceReferenceId);
        Assert.Equal("bpmn-source:" + BpmnModelIdentity.Create("", "author", "same-operation"), raw.SourceReferenceId);
        Assert.Equal(raw.SourceReferenceId, rawSource.SourceId);
        Assert.Equal("1", rawSource.SourceVersion);

        var retained = (await sources.FindAsync(published.SourceReferenceId))!;
        Assert.Equal(source.VersionId, retained.SourceId);
        Assert.Equal(source.Version, retained.SourceVersion);
        Assert.Equal(published.Executable.DefinitionId, retained.DefinitionId);
        Assert.Equal(published.Executable.DefinitionVersionId, retained.DefinitionVersionId);
        Assert.Equal(source.Layout[0].AdditionalProperties!.Value.GetRawText(), retained.Layout[0].AdditionalProperties!.Value.GetRawText());
        Assert.Equal(source.ActivityPresentation, retained.ActivityPresentation);
        Assert.Equal(published, await runtime.PublishFromSourceAsync(definition, PublicationSource(), "same-operation", caller));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(retained, (await sources.FindAsync(published.SourceReferenceId))!));

        var anotherVersion = await runtime.PublishFromSourceAsync(definition, PublicationSource("version-2"), "second-publication", caller);
        Assert.Equal(published.Executable, anotherVersion.Executable);
        Assert.NotEqual(published.SourceReferenceId, anotherVersion.SourceReferenceId);
        Assert.Equal("version-2", (await sources.FindAsync(anotherVersion.SourceReferenceId))!.SourceId);
        await AssertNativeEmptyAsync(scope.ServiceProvider);
    }

    [Theory]
    [InlineData("version-id")]
    [InlineData("version")]
    [InlineData("layout")]
    [InlineData("presentation")]
    public async Task Published_authored_operation_refuses_changed_provenance_without_replacing_the_winner(string changed)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var caller = new BpmnModelCaller("author", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var published = await runtime.PublishFromSourceAsync(definition, PublicationSource(), "publish", caller);
        var before = (await sources.FindAsync(published.SourceReferenceId))!;
        var candidate = PublicationSource(changed == "version-id" ? "different-version" : "version-1",
            changed == "version" ? "2.0.0" : "1.0.0", changed == "layout" ? 100 : 10,
            changed == "presentation" ? "Different name" : "Review order");
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
            runtime.PublishFromSourceAsync(definition, candidate, "publish", caller).AsTask());
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(before, (await sources.FindAsync(published.SourceReferenceId))!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_authored_source_recovers_from_runtime_store_without_design_or_new_admission(bool canonicalDefinition)
    {
        BpmnPublishedModel published;
        BpmnModelCommandOutcome started;
        var caller = new BpmnModelCaller("author", null);
        await using (var host = await OpenAsync())
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            published = await runtime.PublishFromSourceAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!,
                PublicationSource(definitionId: canonicalDefinition ? "orders" : null), "publish", caller);
            started = await runtime.StartAsync(published.SourceReferenceId, "start", null, caller);
            Assert.Single((await runtime.ListWorkAsync(started.ExecutionId, new(), caller)).Items);
            if (canonicalDefinition)
                Assert.True(await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
                    .RetireAsync(published.SourceReferenceId, DateTimeOffset.UtcNow, "owner-retired"));
        }
        await using (var host = await OpenAsync(admitNewExecutions: false))
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var source = (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(published.SourceReferenceId))!;
            Assert.Equal("version-1", source.SourceId);
            Assert.Equal("Review order", Assert.Single(source.ActivityPresentation).DisplayName);
            Assert.Equal(canonicalDefinition ? "orders" : null,
                (await runtime.InspectAsync(started.ExecutionId, caller)).Source!.DefinitionId);
            if (canonicalDefinition)
                await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => runtime.StartAsync(published.SourceReferenceId, "new-start", null, caller).AsTask());
            var work = Assert.Single((await runtime.ListWorkAsync(started.ExecutionId, new(), caller)).Items);
            var completed = await runtime.CompleteHumanAsync(started.ExecutionId, "complete", work.InvocationId, work.AttemptId, null, caller);
            Assert.False(completed.Pending);
            Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await runtime.FindExecutionAsync(started.ExecutionId, caller))!.Lifecycle);
            Assert.True((await runtime.CompleteHumanAsync(started.ExecutionId, "complete", work.InvocationId, work.AttemptId, null, caller)).Replay);
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
    }

    [Fact]
    public void Authored_provenance_captures_opaque_layout_before_its_owner_can_dispose_or_mutate_it()
    {
        var presentation = new List<WorkflowExecutableActivityPresentationRecord> { new("root", "Review order") };
        BpmnModelPublicationProvenance source;
        using (var document = JsonDocument.Parse("{\"diagram\":{\"shapes\":{\"human\":{\"x\":10}}}}"))
        {
            var layout = new List<WorkflowExecutableLayoutRecord> { new("root", 10, 20, AdditionalProperties: document.RootElement) };
            source = new("version-1", "1.0.0", layout, presentation);
            layout.Clear();
            presentation.Clear();
        }
        Assert.Single(source.Layout);
        Assert.Equal(10, source.Layout[0].AdditionalProperties!.Value.GetProperty("diagram").GetProperty("shapes").GetProperty("human").GetProperty("x").GetInt32());
        Assert.Equal("Review order", Assert.Single(source.ActivityPresentation).DisplayName);
    }

    [Fact]
    public async Task Canonical_authored_identity_survives_material_changes_without_changing_each_material_pin()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var caller = new BpmnModelCaller("author", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var raw = await runtime.PublishAsync(definition, "raw", caller);
        var first = await runtime.PublishFromSourceAsync(definition, PublicationSource(definitionId: "orders"), "first", caller);
        var second = await runtime.PublishFromSourceAsync(ChangedMaterial(definition), PublicationSource("version-2", definitionId: "orders"), "second", caller);
        Assert.Equal(raw.Executable, first.Executable);
        Assert.NotEqual(first.Executable.ArtifactId, second.Executable.ArtifactId);
        Assert.NotEqual("orders", first.Executable.DefinitionId);
        foreach (var published in new[] { first, second })
        {
            var retained = (await sources.FindAsync(published.SourceReferenceId))!;
            Assert.Equal("BpmnModel.Authored.v1", retained.SourceKind);
            Assert.Equal("orders", retained.DefinitionId);
            Assert.Equal(published.Executable.DefinitionVersionId, retained.DefinitionVersionId);
            Assert.Equal("orders", new BpmnInspectionProvenance(retained).DefinitionId);
        }
        Assert.Equal(first, await runtime.PublishFromSourceAsync(definition, PublicationSource(definitionId: "orders"), "first", caller));
        await AssertAuthoredEmbeddingPinAsync(scope.ServiceProvider, first);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("material")]
    [InlineData("legacy-downgrade")]
    public async Task Authored_publication_identity_conflict_cannot_replace_a_canonical_source(string changed)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var caller = new BpmnModelCaller("author", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var winner = await runtime.PublishFromSourceAsync(definition, PublicationSource(definitionId: "orders"), "publish", caller);
        var before = (await sources.FindAsync(winner.SourceReferenceId))!;
        var candidate = PublicationSource(definitionId: changed == "legacy-downgrade" ? null : changed == "definition" ? "another" : "orders");
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => runtime.PublishFromSourceAsync(
            changed == "material" ? ChangedMaterial(definition) : definition, candidate, "publish", caller).AsTask());
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(before, (await sources.FindAsync(winner.SourceReferenceId))!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_constructor_and_absent_JSON_keep_logical_provenance_absent(bool respectRequiredConstructorParameters)
    {
        var options = new JsonSerializerOptions(Web) { RespectRequiredConstructorParameters = respectRequiredConstructorParameters };
        var legacy = PublicationSource();
        var wire = JsonSerializer.SerializeToElement(legacy, options);
        Assert.False(wire.TryGetProperty("definitionId", out _));
        Assert.Null(wire.Deserialize<BpmnModelPublicationProvenance>(options)!.DefinitionId);
        var minimal = JsonSerializer.Deserialize<BpmnModelPublicationProvenance>(
            """{"versionId":"version-1","version":"1.0.0"}""", options)!;
        Assert.Equal("version-1", minimal.VersionId);
        Assert.Equal("1.0.0", minimal.Version);
        Assert.Null(minimal.DefinitionId);
        Assert.Empty(minimal.Layout);
        Assert.Empty(minimal.ActivityPresentation);
        var authored = PublicationSource(definitionId: "orders");
        Assert.Equal("orders", JsonSerializer.SerializeToElement(authored, options).Deserialize<BpmnModelPublicationProvenance>(options)!.DefinitionId);
        var opaque = PublicationSource(definitionId: "opaque-\ud800");
        Assert.Equal("opaque-\ud800", opaque.DefinitionId);
        Assert.Throws<ArgumentException>(() => PublicationSource(definitionId: "\t\u2003"));
        Assert.Throws<ArgumentException>(() => BpmnModelPublicationProvenance.ForAuthoredDefinition(null!, "version-1", "1.0.0"));
        var minimalAuthored = BpmnModelPublicationProvenance.ForAuthoredDefinition("orders", "version-1", "1.0.0");
        Assert.Empty(minimalAuthored.Layout);
        Assert.Empty(minimalAuthored.ActivityPresentation);
        Assert.Null(new BpmnModelPublicationProvenance("version-1", "1.0.0") { DefinitionId = null }.DefinitionId);
        Assert.Throws<ArgumentException>(() => new BpmnModelPublicationProvenance("version-1", "1.0.0") { DefinitionId = " " });
        Assert.Equal("opaque-\ud800", new BpmnModelPublicationProvenance("version-1", "1.0.0") { DefinitionId = "opaque-\ud800" }.DefinitionId);
    }

    [Fact]
    public void Canonical_authored_BPMN_source_identity_maps_to_Message_without_reencoding_or_hash_identity()
    {
        var scope = new MessageScope(new PersistenceScope("store"));
        var definitions = new HashSet<MessageDefinitionIdentity>();
        foreach (var id in new[] { "authored-\ud800", "authored-\ud801", "authored-\udc00", "authored-\ufffd", "authored\0tail" })
        {
            var source = BpmnModelPublicationProvenance.ForAuthoredDefinition(id, "version", "1.0.0");
            var recipient = MessageDefinitionIdentity.ForWorkflowDefinition(scope, source.DefinitionId!);
            Assert.Equal(id, recipient.DefinitionId);
            Assert.Equal(MessageDefinitionIdentity.WorkflowDefinitionNamespace, recipient.DefinitionNamespace);
            Assert.Equal(new MessageDefinitionIdentity(scope, "WorkflowDefinition", id), recipient);
            definitions.Add(recipient);
        }
        Assert.Equal(5, definitions.Count);
    }

    private static BpmnModelDefinition ChangedMaterial(BpmnModelDefinition definition)
    {
        var changed = JsonSerializer.SerializeToNode(definition, Web)!;
        changed["process"]!["processId"] = "changed-process";
        return changed.Deserialize<BpmnModelDefinition>(Web)!;
    }

    private static async Task AssertAuthoredEmbeddingPinAsync(IServiceProvider services, BpmnPublishedModel published)
    {
        var type = new ValueTypeDescriptor(typeof(string).FullName!, schemaVersion: 1);
        var node = new ExecutableNode("run", "run", BpmnActivityIdentities.RunType, "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { [BpmnActivityIdentities.ModelInput] = new(BpmnActivityIdentities.ModelInput,
                type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(published.SourceReferenceId), ValueProtectionPolicy.InstanceInline)) },
            new Dictionary<string, string>());
        var at = DateTimeOffset.UtcNow;
        var request = new WorkflowExecutableCompileRequest("native-version", WorkflowExecutableReferenceScope.Published, at, at, null, "artifact-");
        var source = new WorkflowExecutableCompileSource("parent-definition", "native-version", "1.0.0", WorkflowDefinitionState.Empty,
            "WorkflowDefinitionVersion", "native-version", "1.0.0");
        var contribution = await new BpmnEmbeddingPinSource(services.GetRequiredService<IWorkflowExecutableStore>(),
            services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), TimeProvider.System)
            .GetContributionAsync(new(request, source, node));
        Assert.Equal(published.Executable.ArtifactId, Assert.Single(contribution.Dependencies).ArtifactId);
    }

    private static BpmnModelPublicationProvenance PublicationSource(string id = "version-1", string version = "1.0.0",
        double x = 10, string name = "Review order", string? definitionId = null)
    {
        WorkflowExecutableLayoutRecord[] layout =
            [new("root", x, 20, AdditionalProperties: JsonSerializer.SerializeToElement(new { diagram = new { shapes = new { human = new { x } } } }))];
        WorkflowExecutableActivityPresentationRecord[] presentation = [new("root", name)];
        return definitionId is null ? new(id, version, layout, presentation) :
            BpmnModelPublicationProvenance.ForAuthoredDefinition(definitionId, id, version, layout, presentation);
    }
}
