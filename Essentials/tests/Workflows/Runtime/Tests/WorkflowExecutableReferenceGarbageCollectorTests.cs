using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableReferenceGarbageCollectorTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _sourceReferenceStore = new();
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore = new InMemoryWorkflowExecutionStateStore();

    [Fact]
    public void Legacy_collector_constructor_does_not_claim_saved_cohort_root_support() =>
        Assert.False(NewGarbageCollector().SupportsStimulusDeliveryRetention);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Default_DI_selects_qualified_cohort_retention_and_keeps_ordinary_template_cleanup(bool pumpFirst)
    {
        var services = new ServiceCollection().AddLogging();
        if (pumpFirst) new WorkflowsRuntimeReferenceGarbageCollectionFeature().ConfigureServices(services);
        services.AddWorkflowRuntime();
        if (!pumpFirst) new WorkflowsRuntimeReferenceGarbageCollectionFeature().ConfigureServices(services);
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var runtime = scope.ServiceProvider;
        Assert.NotNull(runtime.GetRequiredService<IStimulusDeliveryStore>());
        var templates = runtime.GetRequiredService<IExecutableActivityTemplateStore>();
        var references = runtime.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var template = Template("ordinary-retired");
        await templates.SaveAsync(template);
        await references.SaveAsync(Reference("ordinary-ref", template.TemplateId, WorkflowExecutableReferenceScope.Published));
        Assert.True(await references.RetireAsync("ordinary-ref", _now));
        var collector = runtime.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();

        Assert.True(collector.SupportsStimulusDeliveryRetention);
        var result = await collector.SweepAsync(_now);
        Assert.Equal(1, result.DeletedReferenceCount); Assert.Equal(1, result.DeletedActivityTemplateCount);
        Assert.Null(await templates.FindAsync(template.TemplateId)); Assert.Null(await references.FindAsync("ordinary-ref"));
    }

    [Fact]
    public async Task Sweep_RemovesExpiredReferencesThenTheirNowUnreferencedArtifacts_ButKeepsArtifactsWithALiveReference()
    {
        // Test (c). "kept" has a live Published reference; "doomed" has only an expired TestRun reference. One sweep
        // drops the expired reference and then prunes the artifact it left unreferenced, while "kept" survives.
        await _executableStore.SaveAsync(Executable("artifact-kept"));
        await _executableStore.SaveAsync(Executable("artifact-doomed"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-live", "artifact-kept", WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.SaveAsync(Reference("ref-expired", "artifact-doomed", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(-1)));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(1, result.DeletedArtifactCount);
        Assert.True(result.DidWork);
        Assert.NotNull(await _executableStore.FindAsync("artifact-kept"));
        Assert.Null(await _executableStore.FindAsync("artifact-doomed"));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("ref-live"));
        Assert.Null(await _sourceReferenceStore.FindAsync("ref-expired"));
    }

    [Fact]
    public async Task Sweep_PrunesArtifactWhoseOnlyReferenceWasRetired()
    {
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.RetireAsync("ref-1", _now, "deleted");

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(1, result.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync("artifact-1"));
    }

    [Fact]
    public async Task Sweep_KeepsArtifactThatStillHasAnotherLiveReference()
    {
        // Two references point at the same artifact; retiring one must not prune the artifact while the other is live.
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-live", "artifact-1", WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.SaveAsync(Reference("ref-retired", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(-1)));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync("artifact-1"));
    }

    [Fact]
    public async Task Sweep_IsANoOpWhenNothingIsExpiredOrRetired()
    {
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.False(result.DidWork);
        Assert.NotNull(await _executableStore.FindAsync("artifact-1"));
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Pending)]
    [InlineData(WorkflowExecutionStatus.Running)]
    [InlineData(WorkflowExecutionStatus.Suspended)]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task Sweep_KeepsArtifactPinnedByAnyRetainedExecutionStatus(WorkflowExecutionStatus status)
    {
        var artifactId = $"artifact-{status}";
        await _executableStore.SaveAsync(Executable(artifactId));
        await _workflowExecutionStateStore.SaveAsync(Execution($"execution-{status}", artifactId, status));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync(artifactId));
    }

    [Fact]
    public async Task Sweep_CollectsArtifactOnlyAfterItsFinalRetainedExecutionRootIsRemoved()
    {
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-1", "artifact-1", WorkflowExecutionStatus.Completed));

        var protectedSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(0, protectedSweep.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync("artifact-1"));

        Assert.True(await _workflowExecutionStateStore.DeleteAsync("execution-1"));

        var collectibleSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, collectibleSweep.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync("artifact-1"));
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Pending)]
    [InlineData(WorkflowDispatchStatus.Started)]
    public async Task Sweep_keeps_artifact_pinned_by_nonterminal_dispatch(WorkflowDispatchStatus status)
    {
        const string artifactId = "artifact-dispatched";
        await _executableStore.SaveAsync(Executable(artifactId));
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var pending = new WorkflowDispatchRecord(
            identity.DispatchId,
            "parent-1",
            "activity-1",
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
            new WorkflowExecutableSourceProvenance("source-1", "WorkflowDefinitionVersion", "version-1", "1.0.0", "definition-1", "version-1", "1.0.0", "publication-1", null),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition("default"),
            WorkflowRunKind.PublishedRun,
            WorkflowExecutionAuthoritySnapshot.CreateRoot("test"),
            [],
            _now,
            _now);
        await dispatchStore.SaveAsync(pending);
        if (status == WorkflowDispatchStatus.Started)
            await dispatchStore.SaveAsync(pending.TransitionTo(status, _now.AddSeconds(1)));

        var result = await NewGarbageCollector(dispatchStore).SweepAsync(_now);

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync(artifactId));
    }

    [Fact]
    public async Task Sweep_RetainsReusableActivityTemplateUntilItsFinalExecutionRootIsRemoved()
    {
        var templates = new InMemoryExecutableActivityTemplateStore();
        var template = new ExecutableActivityTemplate(
            "template-1", "hash-template-1", Executable("unused").RootActivity,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "test/1",
            new Dictionary<string, string>(), _now);
        await templates.SaveAsync(template);
        var baseExecutable = Executable("artifact-1");
        var executable = new WorkflowExecutable(
            baseExecutable.Identity,
            baseExecutable.RootActivity,
            baseExecutable.ResumeTargets,
            baseExecutable.CreatedAt,
            new Dictionary<string, string> { ["activity.templateHash"] = template.TemplateHash },
            baseExecutable.IncidentStrategy);
        await _executableStore.SaveAsync(executable);
        await _sourceReferenceStore.SaveAsync(Reference("wrapper-ref", executable.Identity.ArtifactId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _sourceReferenceStore.SaveAsync(Reference("template-ref", template.TemplateId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-1", executable.Identity.ArtifactId, WorkflowExecutionStatus.Running));
        var collector = new WorkflowExecutableReferenceGarbageCollector(
            _executableStore,
            _sourceReferenceStore,
            templates,
            _workflowExecutionStateStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

        var liveSweep = await collector.SweepAsync(_now);

        Assert.Equal(0, liveSweep.DeletedReferenceCount);
        Assert.NotNull(await _executableStore.FindAsync(executable.Identity.ArtifactId));
        Assert.NotNull(await templates.FindAsync(template.TemplateId));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("wrapper-ref"));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("template-ref"));

        await _workflowExecutionStateStore.SaveAsync(Execution("execution-1", executable.Identity.ArtifactId, WorkflowExecutionStatus.Completed));
        var terminalSweep = await collector.SweepAsync(_now);
        Assert.Equal(0, terminalSweep.DeletedArtifactCount);
        Assert.Equal(0, terminalSweep.DeletedActivityTemplateCount);

        Assert.True(await _workflowExecutionStateStore.DeleteAsync("execution-1"));
        var collectedSweep = await collector.SweepAsync(_now);

        Assert.Equal(2, collectedSweep.DeletedReferenceCount);
        Assert.Equal(1, collectedSweep.DeletedArtifactCount);
        Assert.Equal(1, collectedSweep.DeletedActivityTemplateCount);
        Assert.Null(await _sourceReferenceStore.FindAsync("wrapper-ref"));
        Assert.Null(await _sourceReferenceStore.FindAsync("template-ref"));
        Assert.Null(await templates.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData("wrapper", "none")]
    [InlineData("node", "none")]
    [InlineData("nested", "none")]
    [InlineData("wrapper", "closed")]
    [InlineData("node", "closed")]
    [InlineData("nested", "closed")]
    [InlineData("wrapper", "direct")]
    [InlineData("node", "direct")]
    [InlineData("nested", "direct")]
    public async Task Sweep_preserves_exact_template_closure_from_every_retained_placement(string placement, string dependencyKind)
    {
        var templates = new InMemoryExecutableActivityTemplateStore();
        var leaf = Template("leaf");
        var root = Template("root", dependencyKind == "closed" ? [new(leaf.TemplateId, leaf.TemplateHash)] : [],
            dependencyKind == "direct" ? [TemplateDependency(leaf)] : []);
        await templates.SaveAsync(leaf); await templates.SaveAsync(root);
        var metadata = new Dictionary<string, string> { ["activity.templateHash"] = root.TemplateHash };
        ExecutableNode Node(string id, IReadOnlyDictionary<string, string> values, params ExecutableNode[] children) => new(
            id, id, "test/activity", "1.0.0", Executable("unused").RootActivity.Descriptor,
            new Dictionary<string, RuntimeInputBinding>(), values,
            children.Length == 0 ? [] : [new ExecutableChildSlot("body", children)]);
        var node = placement == "nested" ? Node("root", new Dictionary<string, string>(), Node("nested", metadata))
            : Node("root", placement == "node" ? metadata : new Dictionary<string, string>());
        var original = Executable("artifact-template");
        var executable = new WorkflowExecutable(original.Identity, node, original.ResumeTargets, _now,
            placement == "wrapper" ? metadata : new Dictionary<string, string>(), original.IncidentStrategy);
        await _executableStore.SaveAsync(executable);
        await _sourceReferenceStore.SaveAsync(Reference("wrapper-ref", executable.Identity.ArtifactId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _sourceReferenceStore.SaveAsync(Reference("root-ref", root.TemplateId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _sourceReferenceStore.SaveAsync(Reference("leaf-ref", leaf.TemplateId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-template", executable.Identity.ArtifactId, WorkflowExecutionStatus.Completed));
        var collector = TemplateCollector(templates);

        var retained = await collector.SweepAsync(_now);
        Assert.Equal(dependencyKind == "none" ? 1 : 0, retained.DeletedActivityTemplateCount);
        Assert.NotNull(await templates.FindAsync(root.TemplateId));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("root-ref"));
        Assert.Equal(dependencyKind != "none", await templates.FindAsync(leaf.TemplateId) is not null);
        Assert.Equal(dependencyKind != "none", await _sourceReferenceStore.FindAsync("leaf-ref") is not null);
        Assert.NotNull(await _executableStore.FindAsync(executable.Identity.ArtifactId));

        Assert.True(await _workflowExecutionStateStore.DeleteAsync("execution-template"));
        var released = await collector.SweepAsync(_now);
        Assert.Equal(dependencyKind == "none" ? 1 : 2, released.DeletedActivityTemplateCount);
        Assert.Null(await templates.FindAsync(root.TemplateId)); Assert.Null(await templates.FindAsync(leaf.TemplateId));
        Assert.Null(await _sourceReferenceStore.FindAsync("root-ref")); Assert.Null(await _sourceReferenceStore.FindAsync("leaf-ref"));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Sweep_retains_all_material_when_a_present_retained_template_has_an_unreadable_exact_dependency(bool mismatched, bool direct)
    {
        var templates = new InMemoryExecutableActivityTemplateStore();
        var leaf = Template("leaf");
        var identity = new ExecutableActivityTemplateIdentity(leaf.TemplateId, mismatched ? "wrong-hash" : leaf.TemplateHash);
        var root = Template("root", direct ? [] : [identity],
            direct ? [TemplateDependency(leaf) with { TemplateHash = identity.TemplateHash }] : []);
        if (mismatched) await templates.SaveAsync(leaf);
        await templates.SaveAsync(root);
        var original = Executable("artifact-template");
        await _executableStore.SaveAsync(new WorkflowExecutable(original.Identity, original.RootActivity, original.ResumeTargets, _now,
            new Dictionary<string, string> { ["activity.templateHash"] = root.TemplateHash }, original.IncidentStrategy));
        await _sourceReferenceStore.SaveAsync(Reference("root-ref", root.TemplateId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _sourceReferenceStore.SaveAsync(Reference("unrelated-retired", "absent", WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-template", original.Identity.ArtifactId, WorkflowExecutionStatus.Completed));

        Assert.False((await TemplateCollector(templates).SweepAsync(_now)).DidWork);
        Assert.NotNull(await templates.FindAsync(root.TemplateId));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("root-ref"));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("unrelated-retired"));
    }

    [Theory]
    [InlineData("template-metadata")]
    [InlineData("nested-node")]
    [InlineData("mixed-edges")]
    [InlineData("shared-leaf")]
    public async Task Sweep_follows_template_local_and_multilevel_edges_then_releases_the_complete_material(string route)
    {
        var templates = new InMemoryExecutableActivityTemplateStore();
        var leaf = Template("leaf");
        var metadata = new Dictionary<string, string> { ["activity.templateHash"] = leaf.TemplateHash };
        var empty = new Dictionary<string, string>();
        ExecutableNode Node(string id, IReadOnlyDictionary<string, string> values, params ExecutableNode[] children) => new(
            id, id, "test/activity", "1.0.0", Executable("unused").RootActivity.Descriptor,
            new Dictionary<string, RuntimeInputBinding>(), values,
            children.Length == 0 ? [] : [new ExecutableChildSlot("body", children)]);
        var middle = Template("middle",
            route == "shared-leaf" ? [new(leaf.TemplateId, leaf.TemplateHash)] : [],
            route == "mixed-edges" ? [TemplateDependency(leaf)] : [],
            route == "nested-node" ? Node("root", empty, Node("nested", metadata)) : null,
            route == "template-metadata" ? metadata : null);
        var root = Template("root", [new(middle.TemplateId, middle.TemplateHash)],
            route == "shared-leaf" ? [TemplateDependency(leaf)] : []);
        var unrelated = Template("unrelated");
        foreach (var template in new[] { root, middle, leaf, unrelated })
        {
            await templates.SaveAsync(template);
            await _sourceReferenceStore.SaveAsync(Reference(template.TemplateId + "-ref", template.TemplateId,
                WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        }
        var original = Executable("artifact-template");
        await _executableStore.SaveAsync(new WorkflowExecutable(original.Identity, original.RootActivity, original.ResumeTargets, _now,
            new Dictionary<string, string> { ["activity.templateHash"] = root.TemplateHash }, original.IncidentStrategy));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-template", original.Identity.ArtifactId, WorkflowExecutionStatus.Completed));

        var retained = await TemplateCollector(templates).SweepAsync(_now);
        Assert.Equal(1, retained.DeletedActivityTemplateCount); Assert.Equal(1, retained.DeletedReferenceCount);
        foreach (var template in new[] { root, middle, leaf })
        {
            Assert.NotNull(await templates.FindAsync(template.TemplateId));
            Assert.NotNull(await _sourceReferenceStore.FindAsync(template.TemplateId + "-ref"));
        }
        Assert.Null(await templates.FindAsync(unrelated.TemplateId));
        Assert.True(await _workflowExecutionStateStore.DeleteAsync("execution-template"));
        var released = await TemplateCollector(templates).SweepAsync(_now);
        Assert.Equal(3, released.DeletedActivityTemplateCount); Assert.Equal(3, released.DeletedReferenceCount);
        Assert.Equal(1, released.DeletedArtifactCount);
        Assert.Empty(await templates.ListAllAsync()); Assert.Empty(await _sourceReferenceStore.ListAllAsync());
    }

    private ExecutableActivityTemplate Template(string id, IReadOnlyCollection<ExecutableActivityTemplateIdentity>? closed = null,
        IReadOnlyCollection<ExecutableActivityTemplateDependency>? direct = null, ExecutableNode? root = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new(
        "template-" + id, "hash-template-" + id, root ?? Executable("unused").RootActivity,
        new Dictionary<string, WorkflowExecutableResumeTarget>(), direct ?? [], closed ?? [], [], "test/1", metadata ?? new Dictionary<string, string>(), _now);

    private static ExecutableActivityTemplateDependency TemplateDependency(ExecutableActivityTemplate template) => new(
        "activity-definition", "activity-version", "1.0.0", template.TemplateId, template.TemplateHash, "occurrence",
        new ActivityInvocationOrigin([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "root")]));

    [Fact]
    public async Task Sweep_keeps_inlined_imports_usable_without_installing_their_original_template_store()
    {
        var templates = new InMemoryExecutableActivityTemplateStore();
        var original = Executable("artifact-inline-only");
        await _executableStore.SaveAsync(new WorkflowExecutable(original.Identity, original.RootActivity, original.ResumeTargets, _now,
            new Dictionary<string, string> { ["activity.templateHash"] = "not-installed-on-this-host" }, original.IncidentStrategy));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-inline", original.Identity.ArtifactId, WorkflowExecutionStatus.Completed));
        await _sourceReferenceStore.SaveAsync(Reference("unrelated-expired", "absent", WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));

        var result = await TemplateCollector(templates).SweepAsync(_now);
        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.NotNull(await _executableStore.FindAsync(original.Identity.ArtifactId));
        Assert.Null(await _sourceReferenceStore.FindAsync("unrelated-expired"));
    }

    private WorkflowExecutableReferenceGarbageCollector TemplateCollector(IExecutableActivityTemplateStore templates) => new(
        _executableStore, _sourceReferenceStore, templates, _workflowExecutionStateStore,
        Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
        TimeProvider.System, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

    /// <summary>
    /// Resolves a claim the split publication design left on reasoning alone: whether the material a split reusable-activity
    /// publication strands is reclaimable by this collector. The strand is what
    /// <c>A_crash_after_the_runtime_commit_leaves_nothing_but_inert_runtime_material</c> pins — a template
    /// and the create-only <see cref="WorkflowExecutableReferenceScope.Published"/> source reference committed
    /// with it, and no design publication.
    /// <para>
    /// It is unreachable, because nothing resolves a template without its publication. It is not
    /// <i>unreferenced</i>, which is the only sense this collector acts on: query 1 drops a reference solely
    /// when it is retired or expired, and a publication's reference is neither. So the strand survives every
    /// sweep until something retires that reference, and reclamation is not automatic.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Sweep_ReclaimsAnInterruptedPublicationsTemplateOnlyOnceItsSourceReferenceIsRetired()
    {
        var templates = new InMemoryExecutableActivityTemplateStore();
        var template = new ExecutableActivityTemplate(
            "template-stranded", "hash-template-stranded", Executable("unused").RootActivity,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "test/1",
            new Dictionary<string, string>(), _now);
        await templates.SaveAsync(template);
        await _sourceReferenceStore.SaveAsync(
            Reference("stranded-ref", template.TemplateId, WorkflowExecutableReferenceScope.Published));
        var collector = new WorkflowExecutableReferenceGarbageCollector(
            _executableStore,
            _sourceReferenceStore,
            templates,
            _workflowExecutionStateStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

        var strandedSweep = await collector.SweepAsync(_now);

        Assert.False(strandedSweep.DidWork);
        Assert.NotNull(await templates.FindAsync(template.TemplateId));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("stranded-ref"));

        await _sourceReferenceStore.RetireAsync("stranded-ref", _now, "publication-interrupted");
        var reclaimSweep = await collector.SweepAsync(_now);

        Assert.Equal(1, reclaimSweep.DeletedReferenceCount);
        Assert.Equal(1, reclaimSweep.DeletedActivityTemplateCount);
        Assert.Null(await templates.FindAsync(template.TemplateId));
    }

    [Fact]
    public async Task Sweep_ProtectsDirectAndTransitiveDependencyClosureUntilFinalRootIsRemoved()
    {
        var grandchild = Executable("artifact-grandchild");
        var child = Executable("artifact-child", grandchild);
        var parent = Executable("artifact-parent", child);
        await SaveAsync(grandchild, child, parent, Executable("artifact-orphan"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-parent", parent.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));

        var protectedSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, protectedSweep.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync(grandchild.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(child.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(parent.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync("artifact-orphan"));

        await _sourceReferenceStore.RetireAsync("ref-parent", _now, "final-root-removed");
        var finalSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(3, finalSweep.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync(grandchild.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync(child.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync(parent.Identity.ArtifactId));
    }

    [Fact]
    public async Task Sweep_DeduplicatesSharedDiamondClosureAndKeepsSharedChildWhileAnyRootRemains()
    {
        var leaf = Executable("artifact-leaf");
        var left = Executable("artifact-left", leaf);
        var right = Executable("artifact-right", leaf);
        var firstRoot = Executable("artifact-root-1", left, right);
        var secondRoot = Executable("artifact-root-2", right);
        await SaveAsync(leaf, left, right, firstRoot, secondRoot);
        await _sourceReferenceStore.SaveAsync(Reference("ref-root-1", firstRoot.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.SaveAsync(Reference("ref-root-2", secondRoot.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));

        Assert.Equal(0, (await NewGarbageCollector().SweepAsync(_now)).DeletedArtifactCount);

        await _sourceReferenceStore.RetireAsync("ref-root-1", _now, "root-removed");
        var oneRootSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(2, oneRootSweep.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync(firstRoot.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync(left.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(leaf.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(right.Identity.ArtifactId));

        await _sourceReferenceStore.RetireAsync("ref-root-2", _now, "final-root-removed");
        Assert.Equal(3, (await NewGarbageCollector().SweepAsync(_now)).DeletedArtifactCount);
    }

    [Fact]
    public async Task Sweep_FailsClosedWhenARootedDependencyIsMissingOrHasTheWrongHash()
    {
        var malformedParent = ExecutableFromEdges(
            "artifact-parent",
            new WorkflowExecutableDependency("artifact-child", "sha256:wrong", ["node-root"]));
        await SaveAsync(malformedParent, Executable("artifact-child"), Executable("artifact-orphan"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-parent", malformedParent.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync("artifact-orphan"));
    }

    [Fact]
    public void DependencyGraph_RejectsExactCyclesWithADeterministicFullIdentityPath()
    {
        var first = ExecutableFromEdges(
            "artifact-a",
            new WorkflowExecutableDependency("artifact-b", Hash("artifact-b"), ["node-root"]));
        var second = ExecutableFromEdges(
            "artifact-b",
            new WorkflowExecutableDependency("artifact-a", Hash("artifact-a"), ["node-root"]));

        var exception = Assert.Throws<WorkflowExecutableDependencyGraphException>(() =>
            WorkflowExecutableDependencyGraph.ResolveClosure([first.Identity], [second, first]));

        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.Cycle, exception.Kind);
        Assert.Contains("artifact-a@sha256:artifact-a -> artifact-b@sha256:artifact-b -> artifact-a@sha256:artifact-a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyGraph_RejectsAMissingArtifactWithTheExpectedFullIdentity()
    {
        var parent = ExecutableFromEdges(
            "artifact-parent",
            new WorkflowExecutableDependency("artifact-missing", "sha256:missing", ["node-root"]));

        var exception = Assert.Throws<WorkflowExecutableDependencyGraphException>(() =>
            WorkflowExecutableDependencyGraph.ResolveClosure([parent.Identity], [parent]));

        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.MissingArtifact, exception.Kind);
        Assert.Contains("artifact-missing@sha256:missing", exception.Message, StringComparison.Ordinal);
    }

    private WorkflowExecutableReferenceGarbageCollector NewGarbageCollector(
        IWorkflowDispatchRetentionRootStore? dispatchRootStore = null) =>
        new(
            _executableStore,
            _sourceReferenceStore,
            _workflowExecutionStateStore,
            dispatchRootStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

    private WorkflowExecutionState Execution(
        string workflowExecutionId,
        string artifactId,
        WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: workflowExecutionId,
            PinnedExecutable: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            Status: status,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: status.IsTerminal() ? _now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        string artifactId,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? expiresAt = null) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: artifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: _now,
            PublishedAt: scope == WorkflowExecutableReferenceScope.Published ? _now : null,
            Scope: scope,
            ExpiresAt: expiresAt);

    private WorkflowExecutable Executable(string artifactId, params WorkflowExecutable[] dependencies) =>
        ExecutableFromEdges(
            artifactId,
            dependencies.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["node-root"])).ToArray());

    private WorkflowExecutable ExecutableFromEdges(string artifactId, params WorkflowExecutableDependency[] dependencies) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "authored-root",
                activityType: "test/activity",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor(
                    "test",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = "test" })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private async Task SaveAsync(params WorkflowExecutable[] executables)
    {
        foreach (var executable in executables)
            await _executableStore.SaveAsync(executable);
    }

    private static string Hash(string artifactId) => $"sha256:{artifactId}";
}
