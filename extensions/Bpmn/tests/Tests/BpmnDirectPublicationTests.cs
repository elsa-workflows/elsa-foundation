using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
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

    [Fact]
    public async Task Published_authored_source_recovers_from_runtime_store_without_design_or_new_admission()
    {
        BpmnPublishedModel published;
        BpmnModelCommandOutcome started;
        var caller = new BpmnModelCaller("author", null);
        await using (var host = await OpenAsync())
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            published = await runtime.PublishFromSourceAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, PublicationSource(), "publish", caller);
            started = await runtime.StartAsync(published.SourceReferenceId, "start", null, caller);
            Assert.Single((await runtime.ListWorkAsync(started.ExecutionId, new(), caller)).Items);
        }
        await using (var host = await OpenAsync(admitNewExecutions: false))
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var source = (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(published.SourceReferenceId))!;
            Assert.Equal("version-1", source.SourceId);
            Assert.Equal("Review order", Assert.Single(source.ActivityPresentation).DisplayName);
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

    private static BpmnModelPublicationProvenance PublicationSource(string id = "version-1", string version = "1.0.0",
        double x = 10, string name = "Review order") => new(id, version,
        [new("root", x, 20, AdditionalProperties: JsonSerializer.SerializeToElement(new { diagram = new { shapes = new { human = new { x } } } }))],
        [new("root", name)]);
}
