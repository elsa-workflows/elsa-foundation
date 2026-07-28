using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class PersistedExpressionAuthoringContextSourceTests
{
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
        var source = new PersistedExpressionAuthoringContextSource(
            new SingleDraftStore(draft),
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
        var source = new PersistedExpressionAuthoringContextSource(
            new SingleDraftStore(draft),
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
        Assert.Equal(ExpressionValueKind.Scalar, context.ExpectedResultShape!.Kind);
        Assert.Equal("policy-2", context.PolicyFingerprint);
        Assert.NotEqual("authenticated", context.PermissionRevision);
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
        var source = new PersistedExpressionAuthoringContextSource(
            new SingleDraftStore(draft),
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
        var source = new PersistedExpressionAuthoringContextSource(
            new SingleDraftStore(draft),
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
        var source = new PersistedExpressionAuthoringContextSource(
            new SingleDraftStore(draft),
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
        var source = new PersistedExpressionAuthoringContextSource(
            new SingleDraftStore(draft),
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

    private static VariableDefinition Variable(string name) => new($"{name}-key", name, new("String"), null, null);

    private sealed class ScopeStructureService(ActivityNode root, ActivityNode target, ActivityNode sibling, VariableDefinition inside, VariableDefinition hidden) : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
            activity.NodeId == root.NodeId ? [new("Children", [sibling, target])] : [];
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
            activity.NodeId == root.NodeId ? [inside] : activity.NodeId == sibling.NodeId ? [hidden] : [];
        public bool SupportsScopedVariables(ActivityNode activity) => activity.NodeId is "root" or "sibling";
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
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
