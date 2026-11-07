using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class PersistedExpressionAuthoringContextSourceTests
{
    [Fact]
    public async Task Existing_public_constructor_does_not_fall_back_when_shared_authority_is_missing()
    {
        var node = new ActivityNode("target", "activity", [], []);
        var structure = new RankedStructureService(node, [], new Dictionary<string, string[]>());
        var draft = new WorkflowDefinitionDraft { Id = "draft", WorkflowDefinitionId = "workflow", State = new([], node, [], [], null) };
        var source = new PersistedExpressionAuthoringContextSource([new SingleDraftStore(draft)], structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)));
        Assert.Null(await source.TryResolveAsync(new(ExpressionToolingContractVersion.V1, "draft", "target", "text", "JavaScript", "document"), new(true), CancellationToken.None));
    }

    [Fact]
    public async Task A_context_filter_can_deny_before_any_source_shape_is_acquired()
    {
        var target = new ActivityNode("target", "activity", [], []);
        var sibling = new ActivityNode("sibling", "activity", [], []);
        var root = new ActivityNode("root", "container", [], []);
        var structure = new RankedStructureService(root, [sibling, target], new Dictionary<string, string[]> { ["target"] = ["sibling"] });
        var draft = new WorkflowDefinitionDraft { Id = "draft", WorkflowDefinitionId = "workflow", State = new([], root, [], [], null) };
        var guard = new SourceShapeGuard();
        var source = CreateSource([new SingleDraftStore(draft)], structure, new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            contextFilters: [new DenyContextFilter()], configure: services => services.AddSingleton<IActivitySerializedShapeSource>(guard));
        Assert.Null(await source.TryResolveAsync(new(ExpressionToolingContractVersion.V1, "draft", "target", "text", "JavaScript", "document"), new(true), CancellationToken.None));
        Assert.Equal(0, guard.Calls);
    }

    [Fact]
    public async Task An_untouched_catalog_input_is_a_valid_expression_target()
    {
        var node = new ActivityNode("target", "activity", [], []);
        var structure = new RankedStructureService(node, [], new Dictionary<string, string[]>());
        var source = SourceFor(node, structure);
        var context = await source.TryResolveAsync(new(ExpressionToolingContractVersion.V1, "draft", "target", "text", "JavaScript", "document"), new(true), CancellationToken.None);
        Assert.NotNull(context);
        Assert.Equal("String", context.ExpectedResultType);
    }
    [Fact]
    public async Task Policy_only_change_invalidates_the_existing_context_revision()
    {
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var root = new ActivityNode(
            "root",
            "container",
            [],
            [],
            new ActivityNodeStructure("elsa.sequence.structure", "1", JsonSerializer.SerializeToElement(new { })));
        var structure = new ScopeStructureService(root, target, new ActivityNode("sibling", "activity", [], []), Variable("inside"), Variable("hidden"));
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft",
            WorkflowDefinitionId = "workflow",
            State = new WorkflowDefinitionState([], root, [], [], null),
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var source = CreateSource(
            [new SingleDraftStore(draft)],
            structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            symbolFilters: [new PolicySymbolFilter()]);
        var service = new ExpressionAuthoringContextService([source]);
        var request = new ResolveExpressionAuthoringContextRequest(
            ExpressionToolingContractVersion.V1,
            "draft",
            "target",
            "text",
            "JavaScript",
            "document");
        var first = await service.ResolveAsync(
            request,
            new(true, "author-1", "permissions-1", "policy-1"),
            CancellationToken.None);

        var afterPolicyChange = await service.ResolveAsync(
            request with { ContextRevision = first.ContextRevision },
            new(true, "author-1", "permissions-1", "policy-2"),
            CancellationToken.None);
        var refreshed = await service.ResolveAsync(
            request,
            new(true, "author-1", "permissions-1", "policy-2"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Contains(first.Payload!.RootSymbols, symbol => symbol.Name == "inside");
        Assert.Equal(ExpressionToolingOutcomeState.Stale, afterPolicyChange.State);
        Assert.True(refreshed.IsSuccess);
        Assert.DoesNotContain(refreshed.Payload!.RootSymbols, symbol => symbol.Name == "inside");
        Assert.NotEqual(first.ContextRevision, afterPolicyChange.ContextRevision);
    }

    [Fact]
    public async Task Exposes_workflow_inputs_and_only_variables_visible_to_the_selected_node()
    {
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var sibling = new ActivityNode("sibling", "activity", [], []);
        var root = new ActivityNode(
            "root",
            "container",
            [],
            [],
            new ActivityNodeStructure("elsa.sequence.structure", "1", JsonSerializer.SerializeToElement(new { })));
        var structure = new ScopeStructureService(root, target, sibling, Variable("inside"), Variable("hidden"));
        var state = new WorkflowDefinitionState(
            [Variable("workflow")],
            root,
            [new InputDefinition("input-key", "input", new TypeReference("String"), null, "Input", null, true)],
            [],
            null);
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft",
            WorkflowDefinitionId = "workflow",
            State = state,
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var activityVersion = new ActivityDefinitionVersion("1.0.0", "activity-definition")
        {
            Id = "activity",
            Inputs = [new InputDefinition("text", "text", new TypeReference("String"), null, "Text", null, false)],
            Outputs = [new OutputDefinition("result", "result", new TypeReference("Int32"), null, "Result", null, false)]
        };
        var source = CreateSource(
            [new SingleDraftStore(draft)],
            structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            new SingleActivityVersionStore(activityVersion),
            [new HostPolicyFilter()]);

        var context = await source.TryResolveAsync(
            new(ExpressionToolingContractVersion.V1, "draft", "target", "text", "JavaScript", "document"),
            new(true, "author-1"),
            CancellationToken.None);

        Assert.NotNull(context);
        Assert.All(["input", "inside", "workflow"], name => Assert.Contains(context!.RootSymbols, symbol => symbol.Name == name));
        Assert.DoesNotContain(context.RootSymbols, symbol => symbol.Name == "hidden");
        var activityResult = Assert.Single(context.RootSymbols.Where(symbol => symbol.SymbolId == "activity-result:sibling:result"));
        Assert.Equal(ExpressionSymbolKind.ActivityResult, activityResult.Kind);
        Assert.Equal("Int32", activityResult.ValueShape!.DisplayName);
        Assert.DoesNotContain(context.RootSymbols, symbol => symbol.SymbolId == "activity-result:target:result");
        Assert.Equal("String", context.Document.ExpectedResultType);
        Assert.Equal("String", context.ExpectedResultType);
        Assert.Equal(ExpressionValueKind.Unknown, context.ExpectedResultShape!.Kind);
        Assert.Equal("policy-2", context.PolicyFingerprint);
        Assert.NotEqual("authenticated", context.PermissionRevision);
    }

    [Fact]
    public async Task A_structure_offers_the_results_it_guarantees_first_in_its_own_order()
    {
        // A compensation-like container: its handler is guaranteed the results of "second" then "first"; the
        // container promises nothing to "first".
        var first = new ActivityNode("first", "activity", [new("text", new(null), null, null, null, null)], []);
        var second = new ActivityNode("second", "activity", [], []);
        var handler = new ActivityNode("handler", "activity", [new("text", new(null), null, null, null, null)], []);
        var root = new ActivityNode("root", "container", [], [], new ActivityNodeStructure("test.ranked", "1", JsonSerializer.SerializeToElement(new { })));
        var structure = new RankedStructureService(root, [first, second, handler], new Dictionary<string, string[]> { ["handler"] = ["second", "first"] });
        var source = SourceFor(root, structure);

        var handlerContext = await source.TryResolveAsync(
            new(ExpressionToolingContractVersion.V1, "draft", "handler", "text", "JavaScript", "document"), new(true, "author-1"), CancellationToken.None);
        var firstContext = await source.TryResolveAsync(
            new(ExpressionToolingContractVersion.V1, "draft", "first", "text", "JavaScript", "document"), new(true, "author-1"), CancellationToken.None);

        Assert.Equal(
            ["activity-result:second:result", "activity-result:first:result"],
            handlerContext!.RootSymbols.Where(symbol => symbol.Kind == ExpressionSymbolKind.ActivityResult).Select(symbol => symbol.SymbolId));
        Assert.DoesNotContain(firstContext!.RootSymbols, symbol => symbol.Kind == ExpressionSymbolKind.ActivityResult);
    }

    [Fact]
    public async Task Context_service_pages_after_host_policy_filtering()
    {
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var structure = new ScopeStructureService(
            target,
            target,
            new ActivityNode("sibling", "activity", [], []),
            Variable("inside"),
            Variable("hidden"));
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft",
            WorkflowDefinitionId = "workflow",
            State = new(
                [],
                target,
                [
                    new("a", "a", new("String"), null, "A", null, true),
                    new("b", "b", new("String"), null, "B", null, true),
                    new("c", "c", new("String"), null, "C", null, true)
                ],
                [],
                null),
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var source = CreateSource(
            [new SingleDraftStore(draft)],
            structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            symbolFilters: [new RemoveSymbolFilter("a")]);
        var service = new ExpressionAuthoringContextService([source]);

        var result = await service.ResolveAsync(
            new(
                ExpressionToolingContractVersion.V1,
                "draft",
                "target",
                "text",
                "JavaScript",
                "document",
                Skip: 1,
                Take: 1),
            new(true, "author-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("c", Assert.Single(result.Payload!.RootSymbols).Name);
    }

    [Fact]
    public async Task Context_source_applies_host_policy_before_the_catalog_safety_bound()
    {
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var structure = new ScopeStructureService(
            target,
            target,
            new ActivityNode("sibling", "activity", [], []),
            Variable("inside"),
            Variable("hidden"));
        var inputs = Enumerable.Range(0, 5_500)
            .Select(index => new InputDefinition($"denied-{index}", $"denied{index}", new("String"), null, "Denied", null, true))
            .Append(new("allowed", "allowed", new("String"), null, "Allowed", null, true))
            .ToArray();
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft",
            WorkflowDefinitionId = "workflow",
            State = new([], target, inputs, [], null),
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var source = CreateSource(
            [new SingleDraftStore(draft)],
            structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            symbolFilters: [new KeepSymbolFilter("allowed")]);

        var context = await source.TryResolveAsync(
            new(ExpressionToolingContractVersion.V1, "draft", "target", "text", "JavaScript", "document"),
            new(true, "author-1"),
            CancellationToken.None);

        Assert.Equal("allowed", Assert.Single(context!.RootSymbols).Name);
    }

    [Fact]
    public async Task Context_source_stops_candidate_enumeration_when_the_authorized_catalog_is_full()
    {
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var structure = new ScopeStructureService(
            target,
            target,
            new ActivityNode("sibling", "activity", [], []),
            Variable("inside"),
            Variable("hidden"));
        var inputs = Enumerable.Range(0, 6_000)
            .Select(index => new InputDefinition($"input-{index}", $"input{index}", new("String"), null, "Input", null, true))
            .ToArray();
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft",
            WorkflowDefinitionId = "workflow",
            State = new([], target, inputs, [], null),
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var filter = new CountingSymbolFilter();
        var source = CreateSource(
            [new SingleDraftStore(draft)],
            structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            symbolFilters: [filter]);

        var context = await source.TryResolveAsync(
            new(ExpressionToolingContractVersion.V1, "draft", "target", "text", "JavaScript", "document"),
            new(true, "author-1"),
            CancellationToken.None);

        Assert.Equal(5_500, context!.RootSymbols.Count);
        Assert.Equal(5_500, filter.Count);
    }

    [Fact]
    public async Task Warm_bounded_context_resolution_p95_is_below_250_milliseconds_for_500_symbols()
    {
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var structure = new ScopeStructureService(
            target,
            target,
            new ActivityNode("sibling", "activity", [], []),
            Variable("inside"),
            Variable("hidden"));
        var inputs = Enumerable.Range(0, 500)
            .Select(index => new InputDefinition($"input-{index}", $"input{index}", new("String"), null, "Input", null, true))
            .ToArray();
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft",
            WorkflowDefinitionId = "workflow",
            State = new([], target, inputs, [], null),
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var source = CreateSource(
            [new SingleDraftStore(draft)],
            structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)));
        var service = new ExpressionAuthoringContextService([source]);
        var request = new ResolveExpressionAuthoringContextRequest(
            ExpressionToolingContractVersion.V1,
            "draft",
            "target",
            "text",
            "JavaScript",
            "document",
            Take: 500);
        var authorization = new ExpressionAuthoringAuthorization(true, "author-1");

        _ = await service.ResolveAsync(request, authorization, CancellationToken.None);
        var samples = new double[40];
        for (var index = 0; index < samples.Length; index++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = await service.ResolveAsync(request, authorization, CancellationToken.None);
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Assert.Equal(500, result.Payload!.RootSymbols.Count);
        }

        Array.Sort(samples);
        var p95 = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1];
        Assert.True(p95 < 250, $"Expected warm context p95 < 250 ms, measured {p95:F2} ms.");
    }

    [Fact]
    public async Task Resolves_projected_boolean_condition_and_declines_consumed_unused_value()
    {
        var node = new ActivityNode("target", "activity", [new("Value", new("old", "JavaScript"), null, null, null, null)], []);
        var structure = new RankedStructureService(node, [], new Dictionary<string, string[]>())
        {
            InputProjection = new([
                new("Case:a:Condition", new InputDefinition("Case:a:Condition", "Condition", new("Boolean"), null, "Condition", null, false),
                    new("foreign-key", new("true", "JavaScript"), null, null, null, null))], ["Value"])
        };
        var source = SourceFor(node, structure);
        var request = new ResolveExpressionAuthoringContextRequest(ExpressionToolingContractVersion.V1, "draft", "target", "Case:a:Condition", "JavaScript", "revision");
        var context = await source.TryResolveAsync(request, new(true, "author"), CancellationToken.None);
        Assert.NotNull(context);
        Assert.Equal("Boolean", context.Document.ExpectedResultType);
        Assert.Equal("Boolean", context.ExpectedResultType);
        Assert.Null(await source.TryResolveAsync(request with { PropertyKey = "Value" }, new(true, "author"), CancellationToken.None));
        Assert.Null(await source.TryResolveAsync(request with { PropertyKey = "Case:deleted:Condition" }, new(true, "author"), CancellationToken.None));
    }

    private static PersistedExpressionAuthoringContextSource SourceFor(ActivityNode root, IActivityStructureService structure)
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft",
            WorkflowDefinitionId = "workflow",
            State = new WorkflowDefinitionState([], root, [], [], null),
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var activityVersion = new ActivityDefinitionVersion("1.0.0", "activity-definition")
        {
            Id = "activity",
            Inputs = [new InputDefinition("text", "text", new TypeReference("String"), null, "Text", null, false)],
            Outputs = [new OutputDefinition("result", "result", new TypeReference("Int32"), null, "Result", null, false)]
        };
        return CreateSource(
            [new SingleDraftStore(draft)],
            structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            new SingleActivityVersionStore(activityVersion));
    }

    private sealed class RankedStructureService(ActivityNode root, ActivityNode[] children, IReadOnlyDictionary<string, string[]> completedBefore) : IActivityStructureService
    {
        public ActivityStructureInputProjection InputProjection { get; init; } = ActivityStructureInputProjection.Empty;
        public ActivityStructureInputProjection ProjectInputs(ActivityNode activity) => InputProjection;
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            activity.NodeId == root.NodeId ? [new("Children", children)] : [];
        public IReadOnlyList<string>? ChildrenCompletedBefore(ActivityNode activity, string childNodeId) =>
            activity.NodeId == root.NodeId && completedBefore.TryGetValue(childNodeId, out var ids) ? ids : null;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }

    private static VariableDefinition Variable(string name) => new($"{name}-key", name, new("String"), null, null);

    private sealed class ScopeStructureService(ActivityNode root, ActivityNode target, ActivityNode sibling, VariableDefinition inside, VariableDefinition hidden) : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            activity.NodeId == root.NodeId ? [new("Children", new[] { sibling, target }.Where(child => child.NodeId != activity.NodeId))] : [];
        public IReadOnlyList<string>? ChildrenCompletedBefore(ActivityNode activity, string childNodeId) =>
            activity.NodeId == root.NodeId && childNodeId == target.NodeId && target.NodeId != root.NodeId ? [sibling.NodeId] : null;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
            activity.NodeId == root.NodeId ? [inside] : activity.NodeId == sibling.NodeId ? [hidden] : [];
        public bool SupportsScopedVariables(ActivityNode activity) => activity.NodeId is "root" or "sibling";
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }

    private static PersistedExpressionAuthoringContextSource CreateSource(
        IEnumerable<IWorkflowDefinitionDraftStore> drafts,
        IActivityStructureService structures,
        ScopedVariablePicker picker,
        IActivityDefinitionVersionStore? versions = null,
        IEnumerable<IExpressionAuthoringContextFilter>? contextFilters = null,
        IEnumerable<IExpressionAuthoringSymbolFilter>? symbolFilters = null,
        Action<IServiceCollection>? configure = null)
    {
        var input = new InputDefinition("text", "text", new("String"), null, "Text", null, false);
        var outputs = versions is null ? Array.Empty<OutputDefinition>()
            : versions.GetAsync("activity").GetAwaiter().GetResult().Outputs.ToArray();
        var services = new ServiceCollection();
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        services.AddSingleton(structures);
        services.AddSingleton<IActivityDefinitionLookup>(new StubActivityCatalog().Add("container").Add("activity", [input], outputs));
        services.AddSingleton<IActivityAuthoringCatalog, FixtureCatalogAuthority>();
        services.AddSingleton<IWorkflowDefinitionAuthorizer, FixtureDefinitionAuthority>();
        configure?.Invoke(services);
        return new(drafts, structures, picker, versions, contextFilters, symbolFilters, services.BuildServiceProvider());
    }

    private sealed class FixtureDefinitionAuthority : IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(definitionId == "workflow");
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
    private sealed class FixtureCatalogAuthority : IActivityAuthoringCatalog
    {
        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityAuthoringCatalogResult(true,
            new[] { "activity", "container" }.Select(id => new ActivityAuthoringCatalogEntry(id, id, "1.0.0", id, null, null, true, null, [])).ToArray()));
    }

    private sealed class DenyContextFilter : IExpressionAuthoringContextFilter
    {
        public ValueTask<ExpressionAuthoringContext?> FilterAsync(ExpressionAuthoringContext context, ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken) => ValueTask.FromResult<ExpressionAuthoringContext?>(null);
    }
    private sealed class SourceShapeGuard : IActivitySerializedShapeSource
    {
        internal int Calls { get; private set; }
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            System.Collections.Immutable.ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Source metadata must follow the context filter."); }
        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            System.Collections.Immutable.ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Source metadata must follow the context filter."); }
    }

    private sealed class SingleDraftStore(WorkflowDefinitionDraft draft) : IWorkflowDefinitionDraftStore
    {
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionDraft?>(draftId == draft.Id ? draft : null);
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionDraft?>(draft);
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>([draft]);
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DesignMetadataRecord>>([]);
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult<DraftWithLayout?>(new(draft, []));
    }

    private sealed class SingleActivityVersionStore(ActivityDefinitionVersion version) : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            versionId == version.Id ? Task.FromResult(version) : throw Elsa.Primitives.Exceptions.EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => GetAsync(versionId, cancellationToken);
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersion?>(null);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
    }

    private sealed class HostPolicyFilter : IExpressionAuthoringContextFilter
    {
        public ValueTask<ExpressionAuthoringContext?> FilterAsync(
            ExpressionAuthoringContext context,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken)
        {
            Assert.Equal("author-1", authorization.SubjectId);
            return ValueTask.FromResult<ExpressionAuthoringContext?>(context with { PolicyFingerprint = "policy-2" });
        }
    }

    private sealed class RemoveSymbolFilter(string symbolName) : IExpressionAuthoringSymbolFilter
    {
        public ValueTask<ExpressionSymbol?> FilterAsync(
            ExpressionSymbol symbol,
            ExpressionAuthoringContext context,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ExpressionSymbol?>(
                string.Equals(symbol.Name, symbolName, StringComparison.Ordinal) ? null : symbol);
    }

    private sealed class KeepSymbolFilter(string symbolName) : IExpressionAuthoringSymbolFilter
    {
        public ValueTask<ExpressionSymbol?> FilterAsync(
            ExpressionSymbol symbol,
            ExpressionAuthoringContext context,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ExpressionSymbol?>(
                string.Equals(symbol.Name, symbolName, StringComparison.Ordinal) ? symbol : null);
    }

    private sealed class CountingSymbolFilter : IExpressionAuthoringSymbolFilter
    {
        public int Count { get; private set; }

        public ValueTask<ExpressionSymbol?> FilterAsync(
            ExpressionSymbol symbol,
            ExpressionAuthoringContext context,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return ValueTask.FromResult<ExpressionSymbol?>(symbol);
        }
    }

    private sealed class PolicySymbolFilter : IExpressionAuthoringSymbolFilter
    {
        public ValueTask<ExpressionSymbol?> FilterAsync(
            ExpressionSymbol symbol,
            ExpressionAuthoringContext context,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ExpressionSymbol?>(
                authorization.PolicyFingerprint == "policy-2" && symbol.Name == "inside" ? null : symbol);
    }
}
