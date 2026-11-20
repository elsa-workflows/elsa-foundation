using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableSourceRootWriteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Native_source_root_capability_and_original_constructors_preserve_exact_ownership()
    {
        var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts);
        Assert.True(sources.CanWriteRootReferences(artifacts)); Assert.False(sources.CanWriteRootReferences(new InMemoryWorkflowExecutableStore()));
        Assert.False(new InMemoryWorkflowExecutableSourceReferenceStore().CanWriteRootReferences(artifacts));
        Assert.NotNull(typeof(InMemoryWorkflowExecutableSourceReferenceStore).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(InMemoryWorkflowExecutableSourceReferenceStore).GetConstructor([typeof(IWorkflowExecutableStore)]));
    }

    [Fact]
    public async Task Native_fenced_source_root_creation_and_adoption_return_the_actual_unchanged_winner()
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var child = Material("child"); var root = Material("root", child); await artifacts.SaveBatchAsync([root, child]); var grants = await Grants(artifacts, clock, root, child);
        var reference = Reference(root);
        Assert.Same(reference, await sources.SaveWithRootWriteLeasesAsync(reference, root.Identity, grants));
        Assert.Same(reference, await sources.SaveWithRootWriteLeasesAsync(reference with { CreatedAt = Now.AddMinutes(1), PublishedAt = Now.AddMinutes(1) }, root.Identity, grants));
        Assert.Same(reference, await sources.FindAsync(reference.SourceReferenceId));
        Assert.Null(await artifacts.TryBeginDeletionAsync(root.Identity.ArtifactId, "gc", Now.AddMinutes(1), Now));
    }

    [Fact]
    public async Task Native_fenced_source_root_preserves_selected_publication_attribution_independent_of_the_content_producer()
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var root = Material("producer-a"); await artifacts.SaveAsync(root); var grants = await Grants(artifacts, clock, root);
        var publication = Reference(root) with { SourceId = "publication-b", SourceVersion = "2", DefinitionId = "definition-b", DefinitionVersionId = "version-b", ArtifactVersion = "published-2" };
        Assert.Same(publication, await sources.SaveWithRootWriteLeasesAsync(publication, root.Identity, grants));
        Assert.Same(publication, await sources.SaveWithRootWriteLeasesAsync(publication with { CreatedAt = Now.AddSeconds(1) }, root.Identity, grants));
        Assert.Equal(root.Identity, (await artifacts.FindAsync(root.Identity.ArtifactId))!.Identity);
        Assert.NotEqual(root.Identity.DefinitionVersionId, (await sources.FindAsync("source"))!.DefinitionVersionId);
    }

    [Fact]
    public async Task Native_fenced_source_root_cannot_publish_a_reference_to_unleased_material()
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var root = Material("root"); await artifacts.SaveAsync(root); var grants = await Grants(artifacts, clock, root);
        await Assert.ThrowsAsync<ArgumentException>(() => sources.SaveWithRootWriteLeasesAsync(Reference(root) with { ArtifactId = "unleased" }, root.Identity, grants).AsTask());
        Assert.Empty(await sources.ListAllAsync());
    }

    [Theory]
    [InlineData("missing")] [InlineData("extra")] [InlineData("duplicate")] [InlineData("owner")]
    [InlineData("token")] [InlineData("expired")] [InlineData("released")] [InlineData("recreated")]
    public async Task Native_fenced_source_root_refuses_incomplete_or_noncurrent_grants_without_source_write(string defect)
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var child = Material("child"); var root = Material("root", child); var other = Material("other"); await artifacts.SaveBatchAsync([root, child, other]);
        var grants = await Grants(artifacts, clock, root, child);
        switch (defect)
        {
            case "missing": grants = [grants[0]]; break;
            case "extra": grants = [.. grants, .. await Grants(artifacts, clock, other)]; break;
            case "duplicate": grants = [grants[0], grants[0]]; break;
            case "owner": grants = [grants[0] with { LeaseId = "other-owner" }, grants[1]]; break;
            case "token": grants = [grants[0] with { ConcurrencyToken = "not-current" }, grants[1]]; break;
            case "expired": clock.Advance(TimeSpan.FromMinutes(2)); break;
            case "released": await artifacts.ReleaseRootWriteLeaseAsync(grants[0]); break;
            case "recreated": await artifacts.DeleteAsync(child.Identity.ArtifactId); await artifacts.SaveAsync(child); break;
        }
        var failure = await Record.ExceptionAsync(() => sources.SaveWithRootWriteLeasesAsync(Reference(root), root.Identity, grants).AsTask());
        if (defect is "missing" or "extra" or "duplicate") Assert.IsAssignableFrom<ArgumentException>(failure);
        else Assert.IsType<WorkflowExecutableRootWriteLeaseLostException>(failure);
        Assert.Empty(await sources.ListAllAsync());
    }

    [Theory]
    [InlineData("artifact")] [InlineData("definition")] [InlineData("version")] [InlineData("artifact-version")] [InlineData("hash")]
    public async Task Native_fenced_source_root_requires_the_full_physical_pin(string defect)
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var root = Material("root"); await artifacts.SaveAsync(root); var grants = await Grants(artifacts, clock, root);
        var pin = defect switch
        {
            "artifact" => root.Identity with { ArtifactId = "other" }, "definition" => root.Identity with { DefinitionId = "other" },
            "version" => root.Identity with { DefinitionVersionId = "other" }, "artifact-version" => root.Identity with { ArtifactVersion = "other" },
            _ => root.Identity with { ArtifactHash = "other" }
        };
        var reference = Reference(root) with { ArtifactId = pin.ArtifactId, DefinitionId = pin.DefinitionId, DefinitionVersionId = pin.DefinitionVersionId, ArtifactVersion = pin.ArtifactVersion };
        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.SaveWithRootWriteLeasesAsync(reference, pin, grants).AsTask());
        Assert.Empty(await sources.ListAllAsync());
    }

    [Theory]
    [InlineData("retired")] [InlineData("expired")] [InlineData("source")] [InlineData("expiry")] [InlineData("layout")] [InlineData("input")]
    public async Task Native_fenced_source_root_refuses_changed_or_nonlive_winning_source_payload(string defect)
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var root = Material("root"); await artifacts.SaveAsync(root); var grants = await Grants(artifacts, clock, root); var candidate = Reference(root);
        var existing = defect switch
        {
            "retired" => candidate.Retire(Now, "retired"), "expired" => candidate with { ExpiresAt = Now },
            "source" => candidate with { SourceId = "other" }, "expiry" => candidate with { ExpiresAt = Now.AddHours(2) },
            "layout" => candidate with { Layout = [new("other", 1, 2)] },
            _ => candidate with { AuthoredInputs = [new("node", "input", "expression", JsonSerializer.SerializeToElement("opaque"))] }
        };
        await sources.SaveAsync(existing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask());
        Assert.Same(existing, await sources.FindAsync(candidate.SourceReferenceId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_fenced_source_root_refuses_nonlive_proposals(bool retired)
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var root = Material("root"); await artifacts.SaveAsync(root); var grants = await Grants(artifacts, clock, root);
        var candidate = retired ? Reference(root).Retire(Now, "retired") : Reference(root) with { ExpiresAt = Now };
        await Assert.ThrowsAsync<InvalidOperationException>(() => sources.SaveWithRootWriteLeasesAsync(candidate, root.Identity, grants).AsTask()); Assert.Empty(await sources.ListAllAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_fenced_source_root_rechecks_grants_after_waiting_for_the_actual_source_gate(bool expired)
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var root = Material("root"); await artifacts.SaveAsync(root); var grants = await Grants(artifacts, clock, root);
        using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => sources.ExecuteUnderReferenceGate(_ => { held.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return true; }));
        Assert.True(held.Wait(TimeSpan.FromSeconds(10)));
        var publish = Task.Run(() => sources.SaveWithRootWriteLeasesAsync(Reference(root), root.Identity, grants).AsTask());
        try
        {
            var artifactGate = (Lock)typeof(InMemoryWorkflowExecutableStore).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(artifacts)!;
            Assert.True(SpinWait.SpinUntil(() => { if (!artifactGate.TryEnter()) return true; artifactGate.Exit(); return false; }, TimeSpan.FromSeconds(10)));
            clock.Advance(TimeSpan.FromSeconds(expired ? 120 : 10)); release.Set();
            if (expired) await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => publish);
            else Assert.Same(await publish, await sources.FindAsync("source"));
            Assert.Equal(expired, await sources.FindAsync("source") is null);
        }
        finally { release.Set(); await holder; }
    }

    [Fact]
    public async Task Native_fenced_source_root_real_manager_and_collector_preserve_the_published_dependency_closure()
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var child = Material("child"); var root = Material("root", child); await artifacts.SaveBatchAsync([root, child]);
        var manager = new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        await manager.ExecuteFencedAsync([root.Identity], "invocation", async (grants, ct) => { await sources.SaveWithRootWriteLeasesAsync(Reference(root), root.Identity, grants, ct); });
        var collector = new WorkflowExecutableReferenceGarbageCollector(artifacts, sources, new InMemoryExecutableActivityTemplateStore(), new InMemoryWorkflowExecutionStateStore(), null, Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }), clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
        Assert.False((await collector.SweepAsync()).DidWork); Assert.NotNull(await artifacts.FindAsync(root.Identity.ArtifactId)); Assert.NotNull(await artifacts.FindAsync(child.Identity.ArtifactId));
    }

    [Fact]
    public async Task Native_fenced_source_root_collection_wins_then_old_grants_cannot_publish_over_recreated_material()
    {
        var clock = new FakeTimeProvider(Now); var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, clock);
        var root = Material("root"); await artifacts.SaveAsync(root); var grants = await Grants(artifacts, clock, root); await artifacts.ReleaseRootWriteLeaseAsync(grants[0]);
        var collector = new WorkflowExecutableReferenceGarbageCollector(artifacts, sources, new InMemoryExecutableActivityTemplateStore(), new InMemoryWorkflowExecutionStateStore(), null, Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }), clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
        await collector.SweepAsync(); Assert.Null(await artifacts.FindAsync(root.Identity.ArtifactId));
        await artifacts.SaveAsync(root);
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => sources.SaveWithRootWriteLeasesAsync(Reference(root), root.Identity, grants).AsTask());
        Assert.Empty(await sources.ListAllAsync());
        await sources.SaveWithRootWriteLeasesAsync(Reference(root), root.Identity, await Grants(artifacts, clock, root)); Assert.NotNull(await sources.FindAsync("source"));
    }

    [Fact]
    public async Task Native_fenced_source_root_propagates_requested_cancellation_before_effects()
    {
        var artifacts = new InMemoryWorkflowExecutableStore(); var sources = new InMemoryWorkflowExecutableSourceReferenceStore(artifacts, new FakeTimeProvider(Now));
        using var stop = new CancellationTokenSource(); stop.Cancel(); var root = Material("root");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sources.SaveWithRootWriteLeasesAsync(Reference(root), root.Identity, [], stop.Token).AsTask()); Assert.Empty(await sources.ListAllAsync());
    }

    [Fact]
    public async Task Legacy_source_root_ports_refuse_without_invoking_the_old_unfenced_writer()
    {
        var raw = new LegacySourceWriter(); IWorkflowExecutableSourceReferenceWriter writer = raw; var root = Material("root");
        Assert.False(writer.CanWriteRootReferences(new InMemoryWorkflowExecutableStore()));
        await Assert.ThrowsAsync<NotSupportedException>(() => writer.SaveWithRootWriteLeasesAsync(Reference(root), root.Identity, []).AsTask());
        Assert.Equal(0, raw.Writes);
    }

    private sealed class LegacySourceWriter : IWorkflowExecutableSourceReferenceWriter
    {
        public int Writes { get; private set; }
        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) { Writes++; return ValueTask.CompletedTask; }
        public ValueTask<bool> RetireAsync(string id, DateTimeOffset at, string? reason = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> TryDeleteDoomedAsync(WorkflowExecutableSourceReference reference, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<IReadOnlyList<WorkflowExecutableRootWriteLease>> Grants(InMemoryWorkflowExecutableStore artifacts, FakeTimeProvider clock, params WorkflowExecutable[] material)
    {
        var result = new List<WorkflowExecutableRootWriteLease>();
        foreach (var item in material) result.Add((await artifacts.TryAcquireRootWriteLeaseAsync(item.Identity.ArtifactId, "invocation", clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow()))!);
        return result;
    }
    private static WorkflowExecutableSourceReference Reference(WorkflowExecutable root) => new("source", root.Identity.ArtifactId, "WorkflowDefinitionVersion", "authoring", "1",
        root.Identity.DefinitionId, root.Identity.DefinitionVersionId, root.Identity.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.TestRun, ExpiresAt: Now.AddHours(1));
    private static WorkflowExecutable Material(string id, params WorkflowExecutable[] children) => new(new(id, "definition", "version", "1", "hash-" + id),
        new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), null,
        children.Select(child => new WorkflowExecutableDependency(child.Identity.ArtifactId, child.Identity.ArtifactHash, ["node"])).ToArray(), [], [], IncidentStrategyBuiltIns.FaultReference);
}
