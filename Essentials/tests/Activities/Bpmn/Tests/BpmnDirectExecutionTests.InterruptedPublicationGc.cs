using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    private readonly ITestOutputHelper log;

    public BpmnDirectExecutionTests(ITestOutputHelper log) => this.log = log;

    [Fact]
    public async Task Bpmn_publish_interrupted_after_model_save_is_classified_against_host_grace_gc()
    {
        var grace = new WorkflowExecutableGarbageCollectionOptions().ArtifactCreationGracePeriod;
        var pumpSweep = TimeSpan.FromMinutes(5);
        var pumpMaxBackoff = TimeSpan.FromMinutes(30);
        Assert.Equal(TimeSpan.FromMinutes(5), grace);

        var caller = new BpmnModelCaller("operator", null);
        var definition = JsonSerializer.Deserialize<BpmnModelDefinition>(Material().Definition, Web)!;
        string artifactId;
        DateTimeOffset createdAt;
        BpmnInterruptedPublicationWitness before;
        BpmnInterruptedPublicationWitness afterGc;
        WorkflowExecutableReferenceSweepResult sweep;
        await using (var host = await OpenAsync(configure: WrapExecutableStoreAfterModelSave))
        {
            await using var scope = await ScopeAsync(host);
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var error = await Assert.ThrowsAsync<PublicationInterruptedAfterModelSaveException>(
                () => runtime.PublishAsync(definition, "interrupt-publish", caller).AsTask());
            Assert.Contains("after the model material save", error.Message);

            var materials = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
            var saved = Assert.Single(await ListMaterialsAsync(materials));
            artifactId = saved.Identity.ArtifactId;
            createdAt = saved.CreatedAt;
            before = await CaptureBpmnAsync(materials, sources, artifactId);

            var collector = CreateCollector(scope.ServiceProvider, grace);
            var sweepNow = createdAt + grace + TimeSpan.FromMinutes(1);
            sweep = await collector.SweepAsync(sweepNow);
            afterGc = await CaptureBpmnAsync(materials, sources, artifactId);
            Assert.False(afterGc.ArtifactPresent, "The sweep must delete the staged artifact before retry classification.");
            Assert.Equal(1, sweep.DeletedArtifactCount);
            Assert.True(createdAt <= sweepNow - grace);
            Assert.Equal(TimeSpan.FromMinutes(5), pumpSweep);
            Assert.Equal(TimeSpan.FromMinutes(30), pumpMaxBackoff);
            log.WriteLine(
                "bpmn interrupted-publication GC: grace={0} pumpSweep={1} pumpMaxBackoff={2} createdAt={3:o} sweepNow={4:o} deletedArtifacts={5} deletedReferences={6}",
                grace, pumpSweep, pumpMaxBackoff, createdAt, sweepNow, sweep.DeletedArtifactCount, sweep.DeletedReferenceCount);
            log.WriteLine("before GC: {0}", before.Describe());
            log.WriteLine("after GC: {0}", afterGc.Describe());
        }

        Assert.True(before.ArtifactPresent, "The interrupt must land after the BPMN model material is persisted.");
        Assert.Empty(before.LiveSourceReferenceIds);
        Assert.NotEqual(DateTimeOffset.MaxValue, createdAt);

        BpmnPublishedModel? retry = null;
        Exception? retryError = null;
        BpmnInterruptedPublicationWitness afterRetry;
        await using (var host = await OpenAsync())
        {
            await using var scope = await ScopeAsync(host);
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            try
            {
                retry = await runtime.PublishAsync(definition, "interrupt-publish-retry", caller);
            }
            catch (Exception exception)
            {
                retryError = exception;
            }

            var materials = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            var sources = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
            afterRetry = await CaptureBpmnAsync(materials, sources, artifactId);
            var classification = Classify(afterGc, afterRetry, retryError, retry);
            log.WriteLine("after retry: {0} retryError={1} retrySource={2} classification={3}",
                afterRetry.Describe(), retryError?.GetType().FullName, retry?.SourceReferenceId, classification);
            Assert.Equal(BpmnInterruptedPublicationGcOutcome.LossSafelyRefusedOrRetryable, classification);
            if (retryError is null)
            {
                Assert.NotNull(retry);
                Assert.True(afterRetry.ArtifactPresent);
                Assert.Contains(artifactId, afterRetry.LiveSourceReferenceArtifactIds);
                Assert.Equal(artifactId, retry.Executable.ArtifactId);
            }
        }

        Assert.Equal(0, sweep.DeletedReferenceCount);
    }

    private static void WrapExecutableStoreAfterModelSave(IServiceCollection services)
    {
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddScoped<IWorkflowExecutableStore>(provider =>
            new InterruptAfterModelSaveStore(provider.GetRequiredService<EfWorkflowExecutableStore>()));
    }

    private static WorkflowExecutableReferenceGarbageCollector CreateCollector(IServiceProvider services, TimeSpan grace) =>
        new(
            services.GetRequiredService<IWorkflowExecutableStore>(),
            services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
            services.GetRequiredService<IExecutableActivityTemplateStore>(),
            services.GetRequiredService<IWorkflowExecutionStateStore>(),
            null,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = grace }),
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

    private static async Task<BpmnInterruptedPublicationWitness> CaptureBpmnAsync(
        IWorkflowExecutableStore materials,
        IWorkflowExecutableSourceReferenceStore sources,
        string artifactId)
    {
        var artifact = await materials.FindMaterialAsync(artifactId);
        var references = await sources.ListAllAsync();
        return new BpmnInterruptedPublicationWitness(
            artifact is not null,
            artifact?.IncarnationId,
            artifact?.CreatedAt,
            references.Select(reference => reference.SourceReferenceId).ToArray(),
            references.Select(reference => reference.ArtifactId).ToArray());
    }

    private static async Task<IReadOnlyList<RuntimeExecutionMaterial>> ListMaterialsAsync(IWorkflowExecutableStore store)
    {
        var items = new List<RuntimeExecutionMaterial>();
        string? continuation = null;
        do
        {
            var page = await store.ListMaterialPageAsync(new RuntimeStorePageRequest(continuationToken: continuation));
            items.AddRange(page.Items);
            continuation = page.NextContinuationToken;
        } while (continuation is not null);

        return items;
    }

    private static BpmnInterruptedPublicationGcOutcome Classify(
        BpmnInterruptedPublicationWitness afterGc,
        BpmnInterruptedPublicationWitness afterRetry,
        Exception? retryError,
        BpmnPublishedModel? retry)
    {
        if (HasUnsafeCommit(afterGc) || HasUnsafeCommit(afterRetry))
            return BpmnInterruptedPublicationGcOutcome.UnsafeCommit;
        if (retryError is null && retry is null)
            return BpmnInterruptedPublicationGcOutcome.UnsafeCommit;
        return BpmnInterruptedPublicationGcOutcome.LossSafelyRefusedOrRetryable;
    }

    private static bool HasUnsafeCommit(BpmnInterruptedPublicationWitness witness) =>
        !witness.ArtifactPresent && witness.LiveSourceReferenceArtifactIds.Count > 0;

    private sealed class InterruptAfterModelSaveStore : WorkflowExecutableStoreDecorator, IWorkflowExecutableStore
    {
        public InterruptAfterModelSaveStore(IWorkflowExecutableStore inner) : base(inner)
        {
        }

        bool IWorkflowExecutableStore.SupportsModelMaterial => Inner.SupportsModelMaterial;
        IWorkflowExecutableStore IWorkflowExecutableStore.GetMaterialStore() => Inner.GetMaterialStore();

        ValueTask<RuntimeExecutionMaterial?> IWorkflowExecutableStore.FindMaterialAsync(
            string artifactId, CancellationToken cancellationToken) =>
            Inner.FindMaterialAsync(artifactId, cancellationToken);

        ValueTask<RuntimeStorePage<RuntimeExecutionMaterial>> IWorkflowExecutableStore.ListMaterialPageAsync(
            RuntimeStorePageRequest request, CancellationToken cancellationToken) =>
            Inner.ListMaterialPageAsync(request, cancellationToken);

        async ValueTask IWorkflowExecutableStore.SaveModelAsync(RuntimeModelMaterial material, CancellationToken cancellationToken)
        {
            await Inner.SaveModelAsync(material, cancellationToken);
            throw new PublicationInterruptedAfterModelSaveException();
        }
    }
}

internal sealed record BpmnInterruptedPublicationWitness(
    bool ArtifactPresent,
    string? IncarnationId,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<string> LiveSourceReferenceIds,
    IReadOnlyList<string> LiveSourceReferenceArtifactIds)
{
    public string Describe() =>
        $"ArtifactPresent={ArtifactPresent}; incarnation={IncarnationId}; createdAt={CreatedAt:o}; liveRefs=[{string.Join(',', LiveSourceReferenceIds)}]; liveRefArtifacts=[{string.Join(',', LiveSourceReferenceArtifactIds)}]";
}

internal enum BpmnInterruptedPublicationGcOutcome
{
    LossSafelyRefusedOrRetryable = 1,
    UnsafeCommit = 2
}

internal sealed class PublicationInterruptedAfterModelSaveException()
    : Exception("Publication was interrupted after the model material save and before the fenced source reference.");
