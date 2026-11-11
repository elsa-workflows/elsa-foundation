using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Adapts the shared data-source authority to language-specific, host-filtered expression symbols.</summary>
public sealed class PersistedExpressionAuthoringContextSource(
    IEnumerable<IWorkflowDefinitionDraftStore> draftStores,
    IActivityStructureService structures,
    ScopedVariablePicker scopedVariables,
    IActivityDefinitionVersionStore? activityVersions = null,
    IEnumerable<IExpressionAuthoringContextFilter>? contextFilters = null,
    IEnumerable<IExpressionAuthoringSymbolFilter>? symbolFilters = null,
    IServiceProvider? serviceProvider = null) : IExpressionAuthoringContextSource
{
    private const int MaximumContextSymbols = 5_500;

    // Preserve both existing public constructors. Missing composed authority is never a legacy-analysis fallback.
    public PersistedExpressionAuthoringContextSource(
        IWorkflowDefinitionDraftStore drafts,
        IActivityStructureService structures,
        ScopedVariablePicker scopedVariables,
        IActivityDefinitionVersionStore? activityVersions = null,
        IEnumerable<IExpressionAuthoringContextFilter>? contextFilters = null,
        IEnumerable<IExpressionAuthoringSymbolFilter>? symbolFilters = null,
        IServiceProvider? serviceProvider = null)
        : this([drafts], structures, scopedVariables, activityVersions, contextFilters, symbolFilters, serviceProvider) { }

    public async ValueTask<ExpressionAuthoringContext?> TryResolveAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized) return null;
        var stores = draftStores.Take(2).ToArray();
        var analyzer = Single<IActivityDataSelectionAnalyzer>();
        var authorizer = Single<IWorkflowDefinitionAuthorizer>();
        var catalog = Single<IActivityAuthoringCatalog>();
        var lookup = Single<IActivityDefinitionLookup>();
        if (stores is not [var drafts] || analyzer is null || authorizer is null || catalog is null || lookup is null) return null;
        if (!await catalog.CanReadAsync(cancellationToken)) return null;
        var visibleCatalog = await catalog.ListAsync(cancellationToken);
        if (!visibleCatalog.Allowed) return null;
        var draft = await drafts.FindByIdAsync(request.WorkflowDraftId, cancellationToken);
        if (draft is null || draft.Id != request.WorkflowDraftId ||
            !await authorizer.CanReadAsync(draft.WorkflowDefinitionId, cancellationToken) ||
            !await catalog.CanReadAsync(cancellationToken)) return null;
        var baseline = WorkflowAuthoringRevision.For(draft);
        var canonical = WorkflowAuthoringRevision.Snapshot(draft.State.ToStateView());
        if (!ActivityDataContextService.CatalogAllows(canonical.State.RootActivity, structures, visibleCatalog, cancellationToken)) return null;
        var scopeRevision = ActivityDataContextService.CatalogScopeRevision(visibleCatalog);
        var candidate = new ActivityDataSelectionDocument("WorkflowDefinition", draft.Id,
            WorkflowAuthoringRevision.Digest([baseline, canonical.Hash]), canonical.State.RootActivity,
            [.. canonical.State.Inputs], [.. canonical.State.Variables], [.. canonical.State.Outputs]);
        var target = new ActivityDataSelectionTarget(request.NodeId, request.PropertyKey);
        var catalogRevision = await ActivityDataContextService.CatalogRevisionAsync(canonical.State.RootActivity, structures, lookup, cancellationToken);
        var first = await analyzer.AnalyzeAsync(candidate, target, new() { IncludeSources = false }, cancellationToken);
        if (first.ContractRevision != candidate.ContractRevision || first.Target is null || !first.Findings.IsEmpty) return null;
        var document = new ExpressionAuthoringDocument(
            $"{draft.Id}:{request.NodeId}:{request.PropertyKey}:{request.ExpressionType}", draft.Id,
            request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision, first.Target.Type.Alias);
        ExpressionAuthoringContext? context = new(ExpressionToolingContractVersion.V1, document, baseline, baseline, [], new(),
            first.Target.Type.Alias, new(first.Target.Type.Alias, ExpressionValueKind.Unknown, first.Target.IsNullable),
            authorization.PolicyFingerprint ?? authorization.PermissionRevision ?? "default",
            authorization.PermissionRevision ?? WorkflowAuthoringRevision.Digest([authorization.SubjectId ?? "authenticated"]));
        foreach (var filter in contextFilters ?? [])
        {
            context = await filter.FilterAsync(context, authorization, cancellationToken);
            if (context is null) return null;
        }

        var symbols = new List<ExpressionSymbol>();
        var revisions = new List<string> { WorkflowAuthoringRevision.JsonDigest(JsonSerializer.SerializeToElement(first,
            WorkflowsDesignJsonOptions.WireContext.ActivityDataSelectionAnalysis)) };
        var inputNames = canonical.State.Inputs.ToDictionary(input => input.ReferenceKey, input => input.Name, StringComparer.Ordinal);
        var remainingMembers = 128;
        for (var skip = 0; ; skip += 100)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await analyzer.AnalyzeAsync(candidate, target, new() { Skip = skip, Take = 100, ShapeLimits = new(4, remainingMembers) }, cancellationToken);
            if (page.ContractRevision != candidate.ContractRevision || page.Target is null || !page.Findings.IsEmpty) return null;
            foreach (var source in page.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                remainingMembers = Math.Max(0, remainingMembers - CountMembers(source.Shape));
                revisions.Add(WorkflowAuthoringRevision.JsonDigest(JsonSerializer.SerializeToElement(source,
                    WorkflowsDesignJsonOptions.WireContext.ActivityDataSelectionSourceFacts)));
                var symbol = await ToSymbol(source, inputNames, canonical.State.RootActivity, cancellationToken);
                if (symbol is null) continue;
                foreach (var filter in symbolFilters ?? [])
                {
                    symbol = await filter.FilterAsync(symbol, context, authorization, cancellationToken);
                    if (symbol is null) break;
                }
                if (symbol is not null) symbols.Add(symbol);
                if (symbols.Count == MaximumContextSymbols) break;
            }
            if (symbols.Count == MaximumContextSymbols || !page.HasMoreSources || skip >= 10000) break;
        }
        // Re-read the actual baseline and authority before returning the filtered metadata.
        if (!await authorizer.CanReadAsync(draft.WorkflowDefinitionId, cancellationToken) || !await catalog.CanReadAsync(cancellationToken)) return null;
        var currentCatalog = await catalog.ListAsync(cancellationToken);
        if (!currentCatalog.Allowed || !ActivityDataContextService.CatalogAllows(canonical.State.RootActivity, structures, currentCatalog, cancellationToken) ||
            ActivityDataContextService.CatalogScopeRevision(currentCatalog) != scopeRevision ||
            await ActivityDataContextService.CatalogRevisionAsync(canonical.State.RootActivity, structures, lookup, cancellationToken) != catalogRevision) return null;
        var latest = await drafts.FindByIdAsync(draft.Id, cancellationToken);
        if (latest is null || latest.WorkflowDefinitionId != draft.WorkflowDefinitionId || WorkflowAuthoringRevision.For(latest) != baseline) return null;
        var permission = context.PermissionRevision ?? "authenticated";
        var policy = context.PolicyFingerprint ?? "default";
        var revision = WorkflowAuthoringRevision.Digest([baseline, canonical.Hash, scopeRevision, catalogRevision,
            ActivityDataContextService.ProviderRevision(serviceProvider!), request.ExpressionType, permission, policy, .. revisions]);
        return context with { RootSymbols = symbols, ContextRevision = revision, SymbolCatalogRevision = revision,
            PermissionRevision = permission, PolicyFingerprint = policy };
    }

    private T? Single<T>() where T : class
    {
        var instances = serviceProvider?.GetServices<T>().Take(2).ToArray();
        return instances is [var instance] ? instance : null;
    }

    private async ValueTask<ExpressionSymbol?> ToSymbol(ActivityDataSelectionSourceFacts source,
        IReadOnlyDictionary<string, string> inputNames, ActivityNode? root, CancellationToken ct)
    {
        var shape = ToShape(source.Shape);
        if (source.Binding?.ExpressionType == "Input")
        {
            var key = source.Id["input:".Length..];
            return new(source.Id, inputNames.GetValueOrDefault(key, source.DisplayName), ExpressionSymbolKind.WorkflowInput, shape);
        }
        if (source.Binding?.ExpressionType == "Variable")
            return new(source.Id, source.DisplayName, ExpressionSymbolKind.Variable, shape);
        if (source.ActivityResultSource is not { } result ||
            source.Availability != Elsa.Workflows.Design.Core.Models.ActivityChildResultAvailability.Definite) return null;
        var producer = result.ProducerNodeId;
        var projection = result.ProjectionKey;
        // An unproven whole result was never an existing named output. Do not invent a symbol for it.
        if (projection == "$result" && source.Shape.State != ActivitySerializedShapeState.Known) return null;
        var name = projection;
        if (projection != "$result")
        {
            var lookup = Single<IActivityDefinitionLookup>();
            var node = Find(root, producer, ct);
            if (lookup is null || node is null) return null;
            var version = await lookup.FindVersion(node.ActivityVersionId, ct);
            var output = version?.Outputs.SingleOrDefault(output => output.ReferenceKey == projection);
            if (output is null) return null;
            name = output.Name;
        }
        return new($"activity-result:{producer}:{projection}", $"{producer}.{name}", ExpressionSymbolKind.ActivityResult, shape, "Activity output");
    }

    private static ExpressionValueShape ToShape(ActivitySerializedShape shape) => new(shape.Type?.Alias,
        shape.State is not (ActivitySerializedShapeState.Known or ActivitySerializedShapeState.Truncated) ? ExpressionValueKind.Unknown : shape.Kind switch {
            ActivitySerializedValueKind.Scalar => ExpressionValueKind.Scalar,
            ActivitySerializedValueKind.Object => ExpressionValueKind.Object,
            ActivitySerializedValueKind.Map => ExpressionValueKind.Map,
            ActivitySerializedValueKind.Collection => ExpressionValueKind.Array,
            _ => ExpressionValueKind.Unknown
        }, shape.IsNullable ?? true, shape.State != ActivitySerializedShapeState.Known,
        shape.Element is null ? null : ToShape(shape.Element),
        shape.MapValue is null ? null : new("String", ExpressionValueKind.Scalar, false),
        shape.MapValue is null ? null : ToShape(shape.MapValue),
        shape.Members.Select(member => new ExpressionValueMember(member.SerializedName, ToShape(member.Shape))).ToArray());

    private static int CountMembers(ActivitySerializedShape shape) => shape.Members.Sum(member => 1 + CountMembers(member.Shape))
        + (shape.Element is { } element ? 1 + CountMembers(element) : 0)
        + (shape.MapValue is { } map ? 1 + CountMembers(map) : 0);

    private ActivityNode? Find(ActivityNode? root, string nodeId, CancellationToken ct)
    {
        var pending = new Stack<ActivityNode>();
        if (root is not null) pending.Push(root);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var node))
        {
            ct.ThrowIfCancellationRequested();
            if (visited.Count >= 10000 || !visited.Add(node.NodeId)) return null;
            if (node.NodeId == nodeId) return node;
            foreach (var child in structures.ProjectChildren(node).SelectMany(slot => slot.Activities)) pending.Push(child);
        }
        return null;
    }
}
