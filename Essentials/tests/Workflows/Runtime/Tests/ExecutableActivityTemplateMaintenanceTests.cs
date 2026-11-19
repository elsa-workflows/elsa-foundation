using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class ExecutableActivityTemplateMaintenanceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    private readonly InMemoryWorkflowExecutableStore _artifacts = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _references;
    private readonly InMemoryExecutableActivityTemplateStore _templates;

    public ExecutableActivityTemplateMaintenanceTests()
    {
        _references = new(_artifacts);
        _templates = InMemoryExecutableActivityTemplateStore.CreateForMaintenance(_artifacts, _references, new FakeTimeProvider(Now));
    }

    [Fact]
    public void Capability_belongs_to_the_exact_selected_native_stores()
    {
        Assert.True(_templates.CanMaintainReferences(_artifacts, _references));
        Assert.False(_templates.CanMaintainReferences(new InMemoryWorkflowExecutableStore(), _references));
        Assert.False(_templates.CanMaintainReferences(_artifacts, new InMemoryWorkflowExecutableSourceReferenceStore(_artifacts)));
    }

    [Fact]
    public async Task Atomic_publication_returns_the_actual_winner_and_collection_cannot_remove_its_material()
    {
        var template = Template("one"); var reference = Reference(template);
        Assert.Same(reference, await _templates.SaveWithReferenceAsync(template, reference));
        Assert.Same(reference, await _templates.SaveWithReferenceAsync(template,
            reference with { CreatedAt = Now.AddMinutes(1), PublishedAt = Now.AddMinutes(1) }));
        Assert.False(await _templates.TryDeleteUnreferencedAsync(template));
        Assert.False(await _templates.TryDeleteDoomedReferenceAsync(reference, Now));
        Assert.Same(template, await _templates.FindByHashAsync(template.TemplateHash));
        Assert.Same(reference, await _references.FindAsync(reference.SourceReferenceId));
    }

    [Fact]
    public async Task Collection_wins_before_atomic_publication_then_publication_recreates_material_with_its_live_root()
    {
        var template = Template("one"); await _templates.SaveAsync(template);
        Assert.True(await _templates.TryDeleteUnreferencedAsync(template));
        Assert.Null(await _templates.FindAsync(template.TemplateId));
        var reference = Reference(template);
        Assert.Same(reference, await _templates.SaveWithReferenceAsync(template, reference));
        Assert.Same(template, await _templates.FindAsync(template.TemplateId));
        Assert.Same(reference, await _references.FindAsync(reference.SourceReferenceId));
        Assert.False(await _templates.TryDeleteUnreferencedAsync(template));
    }

    [Theory]
    [InlineData("retired")] [InlineData("expired")] [InlineData("different-source")]
    public async Task Atomic_publication_refuses_stale_or_conflicting_reference_before_saving_material(string defect)
    {
        var template = Template("one"); var proposed = Reference(template);
        var existing = defect switch
        {
            "retired" => proposed.Retire(Now, "retired"),
            "expired" => proposed with { ExpiresAt = Now },
            _ => proposed with { SourceId = "foreign" }
        };
        await _references.SaveAsync(existing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _templates.SaveWithReferenceAsync(template, proposed).AsTask());
        Assert.Null(await _templates.FindAsync(template.TemplateId));
        Assert.Same(existing, await _references.FindAsync(proposed.SourceReferenceId));
    }

    [Fact]
    public async Task An_expired_first_proposal_cannot_create_material_or_reference()
    {
        var template = Template("one"); var reference = Reference(template) with { ExpiresAt = Now };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _templates.SaveWithReferenceAsync(template, reference).AsTask());
        Assert.Null(await _templates.FindAsync(template.TemplateId));
        Assert.Null(await _templates.FindByHashAsync(template.TemplateHash));
        Assert.Empty(await _references.ListAllAsync());
    }

    [Fact]
    public async Task Material_conflict_cannot_write_a_source_reference()
    {
        var template = Template("one"); await _templates.SaveAsync(template);
        var conflicting = Template("one", hash: "different");
        var reference = Reference(conflicting);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _templates.SaveWithReferenceAsync(conflicting, reference).AsTask());
        Assert.Null(await _references.FindAsync(reference.SourceReferenceId));
        Assert.Same(template, await _templates.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("\t")]
    public async Task A_malformed_reference_is_refused_before_any_template_or_hash_is_saved(string id)
    {
        var template = Template("one");
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _templates.SaveWithReferenceAsync(template,
            Reference(template) with { SourceReferenceId = id }).AsTask());
        Assert.Null(await _templates.FindAsync(template.TemplateId));
        Assert.Null(await _templates.FindByHashAsync(template.TemplateHash));
        Assert.Empty(await _references.ListAllAsync());
        await _templates.SaveWithReferenceAsync(template, Reference(template));
        Assert.Same(template, await _templates.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData("wrapper")] [InlineData("node")] [InlineData("nested")]
    public async Task A_physical_workflow_saved_after_candidate_discovery_preserves_template_and_reference_without_any_runtime_root(string route)
    {
        var template = Template("one"); await _templates.SaveAsync(template);
        var retired = Reference(template).Retire(Now, "retired"); await _references.SaveAsync(retired);
        var candidate = (await _templates.ListAllAsync()).Single();
        var metadata = new Dictionary<string, string> { ["activity.templateHash"] = template.TemplateHash };
        var node = route == "nested" ? Node("root", children: [Node("boundary", metadata)]) : Node("root", route == "node" ? metadata : null);
        await _artifacts.SaveAsync(new(new("artifact", "definition", "version", "1", "hash"), node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now,
            route == "wrapper" ? metadata : new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));

        Assert.False(await _templates.TryDeleteDoomedReferenceAsync(retired, Now));
        Assert.False(await _templates.TryDeleteUnreferencedAsync(candidate));
        Assert.Same(retired, await _references.FindAsync(retired.SourceReferenceId));
        Assert.True(await _artifacts.DeleteAsync("artifact")); // Final workflow material, not merely its execution root.
        Assert.True(await _templates.TryDeleteDoomedReferenceAsync(retired, Now));
        Assert.True(await _templates.TryDeleteUnreferencedAsync(candidate));
    }

    [Theory]
    [InlineData("closed")] [InlineData("direct")] [InlineData("metadata")] [InlineData("nested")]
    public async Task A_physical_parent_template_saved_after_discovery_preserves_its_dependency_until_parent_collection(string route)
    {
        var leaf = Template("leaf"); await _templates.SaveAsync(leaf);
        var retired = Reference(leaf).Retire(Now, "retired"); await _references.SaveAsync(retired);
        var candidate = (await _templates.FindAsync(leaf.TemplateId))!;
        var metadata = new Dictionary<string, string> { ["activity.templateHash"] = leaf.TemplateHash };
        var parent = Template("parent", closed: route == "closed" ? [new(leaf.TemplateId, leaf.TemplateHash)] : [],
            direct: route == "direct" ? [Dependency(leaf)] : [], metadata: route == "metadata" ? metadata : null,
            root: route == "nested" ? Node("root", children: [Node("nested", metadata)]) : null);
        await _templates.SaveAsync(parent);
        Assert.False(await _templates.TryDeleteDoomedReferenceAsync(retired, Now));
        Assert.False(await _templates.TryDeleteUnreferencedAsync(candidate));
        Assert.True(await _templates.TryDeleteUnreferencedAsync(parent));
        Assert.True(await _templates.TryDeleteDoomedReferenceAsync(retired, Now));
        Assert.True(await _templates.TryDeleteUnreferencedAsync(candidate));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_parent_cannot_commit_after_collection_removed_its_exact_dependency(bool direct)
    {
        var leaf = Template("leaf"); await _templates.SaveAsync(leaf);
        Assert.True(await _templates.TryDeleteUnreferencedAsync(leaf));
        var parent = Template("parent", closed: direct ? [] : [new(leaf.TemplateId, leaf.TemplateHash)], direct: direct ? [Dependency(leaf)] : []);
        await Assert.ThrowsAsync<InvalidDataException>(() => _templates.SaveWithReferenceAsync(parent, Reference(parent)).AsTask());
        Assert.Null(await _templates.FindAsync(parent.TemplateId));
        Assert.Null(await _references.FindAsync(Reference(parent).SourceReferenceId));
        Assert.Null(await _templates.FindAsync(leaf.TemplateId));
    }

    [Fact]
    public async Task Legacy_raw_saves_can_stage_parent_first_but_cannot_grant_live_publication_until_exact_dependencies_exist()
    {
        var leaf = Template("leaf"); var parent = Template("parent", closed: [new(leaf.TemplateId, leaf.TemplateHash)]);
        await _templates.SaveAsync(parent); // Preserve existing inert parent-first staging behavior.
        await Assert.ThrowsAsync<InvalidDataException>(() => _templates.SaveWithReferenceAsync(parent, Reference(parent)).AsTask());
        Assert.Same(parent, await _templates.FindAsync(parent.TemplateId));
        Assert.Null(await _references.FindAsync(Reference(parent).SourceReferenceId));
        await _templates.SaveAsync(leaf);
        await _templates.SaveWithReferenceAsync(parent, Reference(parent));
        Assert.NotNull(await _references.FindAsync(Reference(parent).SourceReferenceId));
        Assert.False(await _templates.TryDeleteUnreferencedAsync(leaf));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Restoring_or_replacing_a_reference_invalidates_the_doomed_snapshot(bool replace)
    {
        var template = Template("one"); await _templates.SaveAsync(template);
        var retired = Reference(template).Retire(Now, "retired"); await _references.SaveAsync(retired);
        var current = replace ? retired with { SourceId = "replacement" } : retired with { DeletedAt = null, DeletedReason = null };
        if (replace) await _references.SaveAsync(current);
        else Assert.True(await _references.TryRestoreAsync(retired, current));
        Assert.False(await _templates.TryDeleteDoomedReferenceAsync(retired, Now));
        Assert.False(await _templates.TryDeleteUnreferencedAsync(template));
        Assert.Same(current, await _references.FindAsync(current.SourceReferenceId));
    }

    [Fact]
    public async Task A_retired_reference_must_be_removed_before_its_template_and_a_recreated_template_invalidates_the_old_snapshot()
    {
        var old = Template("one"); await _templates.SaveAsync(old);
        var retired = Reference(old).Retire(Now, "retired"); await _references.SaveAsync(retired);
        Assert.False(await _templates.TryDeleteUnreferencedAsync(old));
        Assert.True(await _templates.TryDeleteDoomedReferenceAsync(retired, Now));
        Assert.True(await _templates.TryDeleteUnreferencedAsync(old));
        var recreated = Template("one"); await _templates.SaveAsync(recreated);
        Assert.False(await _templates.TryDeleteUnreferencedAsync(old));
        Assert.Same(recreated, await _templates.FindAsync(old.TemplateId));
        Assert.True(await _templates.TryDeleteUnreferencedAsync(recreated));
    }

    [Theory]
    [InlineData("legacy")] [InlineData("foreign-artifacts")] [InlineData("foreign-references")]
    [InlineData("unbound-references")]
    public async Task Unsupported_or_mixed_native_composition_refuses_before_effects(string defect)
    {
        var templates = defect == "legacy" ? new InMemoryExecutableActivityTemplateStore() : InMemoryExecutableActivityTemplateStore.CreateForMaintenance(
            defect == "foreign-artifacts" ? new InMemoryWorkflowExecutableStore() : _artifacts,
            defect == "foreign-references" ? new InMemoryWorkflowExecutableSourceReferenceStore(_artifacts) :
            defect == "unbound-references" ? new InMemoryWorkflowExecutableSourceReferenceStore() : _references);
        Assert.False(templates.CanMaintainReferences(_artifacts, _references));
        if (defect == "foreign-references") return; // Internally coherent with its own source store, never with the selected one.
        var template = Template("one");
        await Assert.ThrowsAsync<NotSupportedException>(() => templates.SaveWithReferenceAsync(template, Reference(template)).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => templates.TryDeleteDoomedReferenceAsync(Reference(template), Now).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => templates.TryDeleteUnreferencedAsync(template).AsTask());
        Assert.Null(await templates.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_and_legacy_default_ports_propagate_requested_cancellation_before_effects(bool legacy)
    {
        IExecutableActivityTemplateStore store = legacy ? new LegacyStore() : _templates;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var template = Template("one");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveWithReferenceAsync(template, Reference(template), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.TryDeleteDoomedReferenceAsync(Reference(template), Now, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.TryDeleteUnreferencedAsync(template, cancellation.Token).AsTask());
        Assert.Null(await store.FindAsync(template.TemplateId));
    }

    [Fact]
    public async Task Legacy_default_ports_refuse_without_material_or_reference_fallback()
    {
        IExecutableActivityTemplateStore store = new LegacyStore(); var template = Template("one");
        Assert.False(store.CanMaintainReferences(_artifacts, _references));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveWithReferenceAsync(template, Reference(template)).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => store.TryDeleteDoomedReferenceAsync(Reference(template), Now).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => store.TryDeleteUnreferencedAsync(template).AsTask());
    }

    private sealed class LegacyStore : IExecutableActivityTemplateStore
    {
        public ValueTask<ExecutableActivityTemplate?> FindAsync(string id, CancellationToken cancellationToken = default) => ValueTask.FromResult<ExecutableActivityTemplate?>(null);
        public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string hash, CancellationToken cancellationToken = default) => ValueTask.FromResult<ExecutableActivityTemplate?>(null);
        public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RuntimeStorePage<ExecutableActivityTemplate>(request, [], null));
        public ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Legacy fallback invoked.");
    }

    private static WorkflowExecutableSourceReference Reference(ExecutableActivityTemplate template) => new("reference-" + template.TemplateId,
        template.TemplateId, "ActivityDefinitionVersion", "version", "1", "definition", "version", "1", Now, Now,
        WorkflowExecutableReferenceScope.Published);
    private static ExecutableActivityTemplateDependency Dependency(ExecutableActivityTemplate template) => new("definition", "version", "1",
        template.TemplateId, template.TemplateHash, "occurrence", ActivityInvocationOrigin.Empty);
    private static ExecutableActivityTemplate Template(string id, string? hash = null,
        IReadOnlyCollection<ExecutableActivityTemplateIdentity>? closed = null, IReadOnlyCollection<ExecutableActivityTemplateDependency>? direct = null,
        IReadOnlyDictionary<string, string>? metadata = null, ExecutableNode? root = null) => new(id, hash ?? "hash-" + id, root ?? Node("root"),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), direct ?? [], closed ?? [], [], "test/1", metadata ?? new Dictionary<string, string>(), Now);
    private static ExecutableNode Node(string id, IReadOnlyDictionary<string, string>? metadata = null, params ExecutableNode[] children) => new(id, id,
        "test", "1", new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding>(), metadata ?? new Dictionary<string, string>(), children.Length == 0 ? [] : [new ExecutableChildSlot("body", children)]);
}
