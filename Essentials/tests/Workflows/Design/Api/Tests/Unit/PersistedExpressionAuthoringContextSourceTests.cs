using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
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
        var source = new PersistedExpressionAuthoringContextSource(
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
        Assert.Equal(ExpressionValueKind.Scalar, context.ExpectedResultShape!.Kind);
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
        var source = new PersistedExpressionAuthoringContextSource(
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
        var source = new PersistedExpressionAuthoringContextSource(
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
        var source = new PersistedExpressionAuthoringContextSource(
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
        var source = new PersistedExpressionAuthoringContextSource(
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

    [Fact]
    public async Task An_activity_owner_reads_only_its_authorized_body_and_invalidates_on_its_revision()
    {
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var earlier = new ActivityNode("earlier:node", "activity", [], []);
        var root = new ActivityNode("root", "container", [], [], new ActivityNodeStructure("elsa.sequence.structure", "1", JsonSerializer.SerializeToElement(new { })));
        var structure = new ScopeStructureService(root, target, earlier, Variable("inside"), Variable("hidden"));
        var state = new WorkflowDefinitionState([Variable("graph")], root,
            [new InputDefinition("a", "A", new("Int32"), null, "A", null, false)], [], null);
        var workflowStore = new SingleDraftStore(new WorkflowDefinitionDraft { Id = "draft", WorkflowDefinitionId = "workflow", State = state with
            { Inputs = [new InputDefinition("caller", "CallerOnly", new("Int32"), null, "CallerOnly", null, false)] } });
        var reader = new ActivityReader();
        var projection = new GraphProjection(state);
        var unrelatedProjection = new GraphProjection(null, "another-provider");
        var version = new ActivityDefinitionVersion("1.0.0", "activity-definition") { Id = "activity",
            Outputs = [new OutputDefinition("result:key", "Result", new("Int32"), null, "Result", null, false)] };
        var source = new PersistedExpressionAuthoringContextSource([workflowStore], structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)), new SingleActivityVersionStore(version),
            contextFilters: null, symbolFilters: [new RemoveSymbolFilter("inside")], serviceProvider: null, activityDraftReaders: [reader], activityGraphProjections: [unrelatedProjection, projection]);
        var service = new ExpressionAuthoringContextService([source]);
        var request = ActivityRequest();

        var first = await service.ResolveAsync(request, new(true), CancellationToken.None);
        reader.Revision++;
        var stale = await service.ResolveAsync(request with { ContextRevision = first.ContextRevision }, new(true), CancellationToken.None);
        var refreshed = await service.ResolveAsync(request, new(true), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal("", first.Payload!.Document.WorkflowDraftId);
        Assert.Equal(request.Owner, first.Payload.Document.Owner);
        Assert.StartsWith("activity-draft:draft:", first.Payload.Document.DocumentId, StringComparison.Ordinal);
        Assert.Contains(first.Payload.RootSymbols, symbol => symbol.Name == "A");
        Assert.Contains(first.Payload.RootSymbols, symbol => symbol.Name == "graph");
        Assert.Contains(first.Payload.RootSymbols, symbol => symbol.SymbolId == "activity-result:earlier%3Anode:result%3Akey");
        Assert.DoesNotContain(first.Payload.RootSymbols, symbol => symbol.Name is "CallerOnly" or "inside" or "hidden" || symbol.SymbolId == "activity-result:target:result");
        var resultBinding = Assert.IsType<ActivityResultExpressionParameterBinding>(Assert.Single(first.Payload.RootSymbols,
            symbol => symbol.SymbolId == "activity-result:earlier%3Anode:result%3Akey").ParameterBinding);
        Assert.Equal("earlier:node", resultBinding.ProducerNodeId);
        Assert.Equal("result:key", resultBinding.ProjectionKey);
        Assert.Equal(0, workflowStore.ReadCount);
        Assert.Equal(ExpressionToolingOutcomeState.Stale, stale.State);
        Assert.NotEqual(first.ContextRevision, refreshed.ContextRevision);
        Assert.Equal(3, projection.ReadCount);
        Assert.Equal(0, unrelatedProjection.ReadCount);
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(2, true, true)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public async Task Activity_metadata_is_unavailable_without_one_visible_authority_and_an_owned_projection(int readerCount, bool visible, bool projectable)
    {
        var root = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var structure = new ScopeStructureService(root, root, root, Variable("inside"), Variable("hidden"));
        var projection = new GraphProjection(projectable ? new([], root, [], [], null) : null);
        var readers = Enumerable.Range(0, readerCount).Select(_ => new ActivityReader { Visible = visible }).ToArray();
        var source = new PersistedExpressionAuthoringContextSource([], structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)), activityVersions: null, contextFilters: null, symbolFilters: null, serviceProvider: null, activityDraftReaders: readers, activityGraphProjections: [projection]);
        var result = await new ExpressionAuthoringContextService([source]).ResolveAsync(ActivityRequest(), new(true), CancellationToken.None);
        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, result.State);
        Assert.Null(result.Payload);
        if (readerCount != 1 || !visible) Assert.Equal(0, projection.ReadCount);
    }

    [Fact]
    public async Task Colliding_colon_tuples_have_distinct_opaque_symbol_ids_and_exact_existing_bindings()
    {
        var first = new ActivityNode("a:b", "activity", [], []);
        var second = new ActivityNode("a", "activity", [], []);
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var root = new ActivityNode("root", "container", [], [], new ActivityNodeStructure("elsa.sequence.structure", "1", JsonSerializer.SerializeToElement(new { })));
        var later = new ActivityNode("later", "activity", [], []);
        var structure = new RankedStructureService(root, [first, second, target, later], new Dictionary<string, string[]>());
        var version = new ActivityDefinitionVersion("1.0.0", "activity-definition") { Id = "activity", Outputs = [
            new OutputDefinition("c", "C", new("String"), null, "C", null, false),
            new OutputDefinition("b:c", "BC", new("String"), null, "BC", null, false)] };
        var state = new WorkflowDefinitionState([], root, [], [], null);
        var source = new PersistedExpressionAuthoringContextSource([], structure, new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            new SingleActivityVersionStore(version), contextFilters: null, symbolFilters: null, serviceProvider: null, activityDraftReaders: [new ActivityReader()], activityGraphProjections: [new GraphProjection(state)]);
        var result = await new ExpressionAuthoringContextService([source]).ResolveAsync(ActivityRequest(), new(true), CancellationToken.None);
        Assert.True(result.IsSuccess);
        var symbols = result.Payload!.RootSymbols.Where(symbol => symbol.Kind == ExpressionSymbolKind.ActivityResult).ToArray();
        Assert.Equal(4, symbols.Length);
        Assert.DoesNotContain(symbols, symbol => ((ActivityResultExpressionParameterBinding)symbol.ParameterBinding!).ProducerNodeId is "target" or "later");
        Assert.Equal(4, symbols.Select(symbol => symbol.SymbolId).Distinct(StringComparer.Ordinal).Count());
        var left = Assert.IsType<ActivityResultExpressionParameterBinding>(Assert.Single(symbols, symbol => symbol.SymbolId == "activity-result:a%3Ab:c").ParameterBinding);
        var right = Assert.IsType<ActivityResultExpressionParameterBinding>(Assert.Single(symbols, symbol => symbol.SymbolId == "activity-result:a:b%3Ac").ParameterBinding);
        Assert.Equal("a:b", left.ProducerNodeId);
        Assert.Equal("c", left.ProjectionKey);
        Assert.Equal("a", right.ProducerNodeId);
        Assert.Equal("b:c", right.ProjectionKey);
    }

    [Fact]
    public async Task A_placed_graph_exposes_its_public_result_without_opening_its_private_descriptor_body()
    {
        var placed = new ActivityNode("placed", "activity", [], []);
        var target = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var root = new ActivityNode("root", "container", [], [], new ActivityNodeStructure("elsa.sequence.structure", "1", JsonSerializer.SerializeToElement(new { })));
        var structure = new RankedStructureService(root, [placed, target], new Dictionary<string, string[]>());
        var version = new ActivityDefinitionVersion("1.0.0", "private-graph-definition") { Id = "activity",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { rootActivity = new { nodeId = "private-body-node" } }),
            Outputs = [new OutputDefinition("public-result", "PublicResult", new("String"), null, "Public result", null, false)] };
        var state = new WorkflowDefinitionState([], root, [], [], null);
        var source = new PersistedExpressionAuthoringContextSource([], structure, new ScopedVariablePicker(new ScopedVariableResolver(structure)),
            new SingleActivityVersionStore(version), contextFilters: null, symbolFilters: null, serviceProvider: null, activityDraftReaders: [new ActivityReader()], activityGraphProjections: [new GraphProjection(state)]);
        var service = new ExpressionAuthoringContextService([source]);
        var visible = await service.ResolveAsync(ActivityRequest(), new(true), CancellationToken.None);
        var hidden = await service.ResolveAsync(ActivityRequest() with { NodeId = "private-body-node" }, new(true), CancellationToken.None);
        Assert.Contains(visible.Payload!.RootSymbols, symbol => symbol.SymbolId == "activity-result:placed:public-result");
        Assert.DoesNotContain(visible.Payload.RootSymbols, symbol => symbol.SymbolId.Contains("private-body-node", StringComparison.Ordinal));
        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, hidden.State);
        Assert.Null(hidden.Payload);
    }

    [Fact]
    public async Task Feature_factory_resolves_the_activity_source_with_optional_stores_and_selects_only_its_provider()
    {
        var root = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var structure = new ScopeStructureService(root, root, root, Variable("inside"), Variable("hidden"));
        var selected = new GraphProjection(new([], root, [], [], null));
        var other = new GraphProjection(null, "another-provider");
        var services = new ServiceCollection();
        services.AddSingleton<IActivityStructureService>(structure);
        services.AddScoped<IActivityDraftAuthoringReader>(_ => new ActivityReader());
        services.AddScoped<IActivityDraftExpressionGraphProjection>(_ => other);
        services.AddScoped<IActivityDraftExpressionGraphProjection>(_ => selected);
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IExpressionAuthoringContextSource>();
        var context = await source.TryResolveAsync(ActivityRequest(), new(true), CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal(ActivityRequest().Owner, context.Document.Owner);
        Assert.Equal(1, selected.ReadCount);
        Assert.Equal(0, other.ReadCount);
    }

    [Fact]
    public async Task An_unregistered_persisted_provider_does_not_invoke_another_strategy()
    {
        var root = new ActivityNode("target", "activity", [new("text", new(null), null, null, null, null)], []);
        var structure = new ScopeStructureService(root, root, root, Variable("inside"), Variable("hidden"));
        var other = new GraphProjection(new([], root, [], [], null), "another-provider");
        var source = new PersistedExpressionAuthoringContextSource([], structure,
            new ScopedVariablePicker(new ScopedVariableResolver(structure)), null, null, null, null, [new ActivityReader()], [other]);

        var result = await new ExpressionAuthoringContextService([source]).ResolveAsync(ActivityRequest(), new(true), CancellationToken.None);

        Assert.Equal(ExpressionToolingOutcomeState.Unavailable, result.State);
        Assert.Null(result.Payload);
        Assert.Equal(0, other.ReadCount);
    }

    [Fact]
    public void Legacy_constructor_optional_arguments_remain_unambiguous()
    {
        var root = new ActivityNode("root", "activity", [], []);
        var structure = new ScopeStructureService(root, root, root, Variable("inside"), Variable("hidden"));
        var picker = new ScopedVariablePicker(new ScopedVariableResolver(structure));
        IWorkflowDefinitionDraftStore store = new SingleDraftStore(new WorkflowDefinitionDraft
            { Id = "draft", WorkflowDefinitionId = "workflow", State = new([], root, [], [], null) });
        Assert.NotNull(new PersistedExpressionAuthoringContextSource([store], structure, picker));
        Assert.NotNull(new PersistedExpressionAuthoringContextSource([store], structure, picker, null));
        Assert.NotNull(new PersistedExpressionAuthoringContextSource(store, structure, picker));
        Assert.NotNull(new PersistedExpressionAuthoringContextSource(store, structure, picker, null));
    }

    private static ResolveExpressionAuthoringContextRequest ActivityRequest() =>
        new(ExpressionToolingContractVersion.V1, "", "target", "text", "JavaScript", "document") { Owner = new("activity-draft", "draft") };

    private sealed class ActivityReader : IActivityDraftAuthoringReader
    {
        public long Revision { get; set; } = 1;
        public bool Visible { get; init; } = true;
        public ValueTask<ActivityDraftAuthoringSource?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivityDraftAuthoringSource?>(Visible ? new(draftId, Revision,
                new(new("1", [], [], []), new("elsa.activity-graph", "1", JsonSerializer.SerializeToElement(new { })), new Dictionary<string, string>()), "permission-1") : null);
    }

    private sealed class GraphProjection(WorkflowDefinitionState? state, string providerKey = "elsa.activity-graph") : IActivityDraftExpressionGraphProjection
    {
        public string ProviderKey => providerKey;
        public int ReadCount { get; private set; }
        public WorkflowDefinitionState? TryProject(ActivityDefinitionDraftState draft)
        {
            ReadCount++;
            return state;
        }
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
        return new PersistedExpressionAuthoringContextSource(
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
            activity.NodeId == root.NodeId ? [new("Children", [sibling, target])] : [];
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) =>
            activity.NodeId == root.NodeId ? [inside] : activity.NodeId == sibling.NodeId ? [hidden] : [];
        public bool SupportsScopedVariables(ActivityNode activity) => activity.NodeId is "root" or "sibling";
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => throw new NotSupportedException();
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => throw new NotSupportedException();
    }

    private sealed class SingleDraftStore(WorkflowDefinitionDraft draft) : IWorkflowDefinitionDraftStore
    {
        public int ReadCount { get; private set; }
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult<WorkflowDefinitionDraft?>(draftId == draft.Id ? draft : null);
        }
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
