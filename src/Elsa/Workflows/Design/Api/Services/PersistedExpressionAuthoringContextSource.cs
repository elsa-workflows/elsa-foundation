using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Builds a safe context from the current persisted draft; it exposes names and shapes only.</summary>
public sealed class PersistedExpressionAuthoringContextSource(
    IEnumerable<IWorkflowDefinitionDraftStore> draftStores,
    IActivityStructureService structures,
    ScopedVariablePicker scopedVariables,
    IActivityDefinitionVersionStore? activityVersions = null,
    IEnumerable<IExpressionAuthoringContextFilter>? contextFilters = null,
    IEnumerable<IExpressionAuthoringSymbolFilter>? symbolFilters = null,
    IServiceProvider? serviceProvider = null) : IExpressionAuthoringContextSource
{
    private const string SequenceStructureKind = "elsa.sequence.structure";
    private const int MaximumContextSymbols = 5_500;
    private IWellKnownTypeRegistry? WellKnownTypes =>
        serviceProvider?.GetService(typeof(IWellKnownTypeRegistry)) as IWellKnownTypeRegistry;

    public PersistedExpressionAuthoringContextSource(
        IWorkflowDefinitionDraftStore drafts,
        IActivityStructureService structures,
        ScopedVariablePicker scopedVariables,
        IActivityDefinitionVersionStore? activityVersions = null,
        IEnumerable<IExpressionAuthoringContextFilter>? contextFilters = null,
        IEnumerable<IExpressionAuthoringSymbolFilter>? symbolFilters = null,
        IServiceProvider? serviceProvider = null)
        : this([drafts], structures, scopedVariables, activityVersions, contextFilters, symbolFilters, serviceProvider)
    {
    }

    public async ValueTask<ExpressionAuthoringContext?> TryResolveAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var drafts = draftStores.FirstOrDefault();
        if (drafts is null)
            return null;

        var draft = await drafts.FindByIdAsync(request.WorkflowDraftId, cancellationToken);
        if (draft is null || FindNode(draft.State.RootActivity, request.NodeId, cancellationToken) is not { } node ||
            !node.Inputs.Concat(node.Outputs).Any(argument => string.Equals(argument.ReferenceKey, request.PropertyKey, StringComparison.Ordinal)))
            return null;

        var target = await FindTargetAsync(node, request.PropertyKey, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var document = new ExpressionAuthoringDocument(
            $"{draft.Id}:{node.NodeId}:{request.PropertyKey}:{request.ExpressionType}",
            draft.Id,
            node.NodeId,
            request.PropertyKey,
            request.ExpressionType,
            request.DocumentRevision,
            target?.Type.Alias);
        var draftRevision = $"{draft.LastModifiedAt.UtcTicks:x}";
        ExpressionAuthoringContext? context = new(
            ExpressionToolingContractVersion.V1,
            document,
            draftRevision,
            draftRevision,
            [],
            new(),
            target?.Type.Alias,
            target is null ? null : ToValueShape(target.Value.Type, target.Value.IsNullable, cancellationToken),
            PolicyFingerprint: authorization.PolicyFingerprint ?? authorization.PermissionRevision ?? "default",
            PermissionRevision: authorization.PermissionRevision ?? PermissionRevision(authorization.SubjectId));
        foreach (var filter in contextFilters ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            context = await filter.FilterAsync(context, authorization, cancellationToken);
            if (context is null)
                return null;
        }

        var symbols = new List<ExpressionSymbol>(Math.Min(MaximumContextSymbols, 256));
        foreach (var input in draft.State.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = new ExpressionSymbol(
                $"input:{input.ReferenceKey}",
                input.Name,
                ExpressionSymbolKind.WorkflowInput,
                new(input.Type.Alias));
            await AddIfVisibleAsync(candidate);
            if (symbols.Count == MaximumContextSymbols)
                break;
        }
        if (symbols.Count < MaximumContextSymbols)
        {
            foreach (var visible in scopedVariables.GetVisibleVariables(draft.State, node.NodeId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = new ExpressionSymbol(
                    $"variable:{visible.ScopeId}:{visible.Variable.ReferenceKey}",
                    visible.Variable.Name,
                    ExpressionSymbolKind.Variable,
                    new(visible.Variable.Type.Alias));
                await AddIfVisibleAsync(candidate);
                if (symbols.Count == MaximumContextSymbols)
                    break;
            }
        }
        if (symbols.Count < MaximumContextSymbols)
        {
            await foreach (var candidate in GetActivityOutputSymbolsAsync(
                               draft.State.RootActivity,
                               node.NodeId,
                               cancellationToken))
            {
                await AddIfVisibleAsync(candidate);
                if (symbols.Count == MaximumContextSymbols)
                    break;
            }
        }

        context = context with { RootSymbols = symbols };
        var permissionRevision = context.PermissionRevision ?? "authenticated";
        var policyFingerprint = context.PolicyFingerprint ?? "default";
        var contextRevision = ContextRevision(
            draftRevision,
            request.ExpressionType,
            permissionRevision,
            policyFingerprint);
        return context with
        {
            ContextRevision = contextRevision,
            SymbolCatalogRevision = contextRevision,
            PermissionRevision = permissionRevision,
            PolicyFingerprint = policyFingerprint
        };

        async ValueTask AddIfVisibleAsync(ExpressionSymbol candidate)
        {
            ExpressionSymbol? filtered = candidate;
            foreach (var filter in symbolFilters ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                filtered = await filter.FilterAsync(filtered, context, authorization, cancellationToken);
                if (filtered is null)
                    return;
            }
            symbols.Add(filtered);
        }
    }

    private async ValueTask<(TypeReference Type, bool IsNullable)?> FindTargetAsync(ActivityNode node, string propertyKey, CancellationToken cancellationToken)
    {
        if (activityVersions is null)
            return null;

        try
        {
            var version = await activityVersions.GetAsync(node.ActivityVersionId, cancellationToken);
            var input = version.Inputs.FirstOrDefault(candidate => string.Equals(candidate.ReferenceKey, propertyKey, StringComparison.Ordinal));
            if (input is not null)
                return (input.Type, input.IsNullable);
            var output = version.Outputs.FirstOrDefault(candidate => string.Equals(candidate.ReferenceKey, propertyKey, StringComparison.Ordinal));
            return output is null ? null : (output.Type, output.IsNullable);
        }
        catch (EntityNotFoundException)
        {
            return null;
        }
    }

    private async IAsyncEnumerable<ExpressionSymbol> GetActivityOutputSymbolsAsync(
        ActivityNode? root,
        string targetNodeId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (activityVersions is null)
            yield break;

        foreach (var node in DefinitelyAvailableProducers(root, targetNodeId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActivityDefinitionVersion version;
            try
            {
                version = await activityVersions.GetAsync(node.ActivityVersionId, cancellationToken);
            }
            catch (EntityNotFoundException)
            {
                // A missing catalog row cannot disclose a result; baseline Design validation reports it separately.
                continue;
            }
            foreach (var output in version.Outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = $"{node.NodeId}.{output.Name}";
                yield return new(
                    $"activity-result:{node.NodeId}:{output.ReferenceKey}",
                    name,
                    ExpressionSymbolKind.ActivityResult,
                    ToValueShape(output.Type, output.IsNullable, cancellationToken),
                    "Activity output");
            }
        }
    }

    private IReadOnlyList<ActivityNode> DefinitelyAvailableProducers(
        ActivityNode? root,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        if (root is null)
            return [];

        var locations = new List<NodeLocation>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(ActivityNode Node, NodeLocation? Parent, int Order)>();
        pending.Push((root, null, 0));
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(item.Node.NodeId))
                continue;
            var location = new NodeLocation(item.Node, item.Parent, item.Order);
            locations.Add(location);
            var children = structures.ProjectChildren(item.Node)
                .SelectMany(projection => projection.Activities)
                .Select((child, order) => (Child: child, Order: order))
                .ToArray();
            foreach (var child in children.Reverse())
                pending.Push((child.Child, location, child.Order));
        }

        var target = locations.FirstOrDefault(location =>
            string.Equals(location.Node.NodeId, targetNodeId, StringComparison.Ordinal));
        return target is null
            ? []
            : locations
                .Where(candidate => IsDefinitelyAvailable(candidate, target))
                .Select(candidate => candidate.Node)
                .ToArray();
    }

    private static bool IsDefinitelyAvailable(NodeLocation producer, NodeLocation consumer)
    {
        if (ReferenceEquals(producer, consumer))
            return false;

        var producerAncestors = new HashSet<NodeLocation>();
        for (var current = producer.Parent; current is not null; current = current.Parent)
            producerAncestors.Add(current);
        NodeLocation? common = null;
        for (var current = consumer.Parent; current is not null; current = current.Parent)
        {
            if (!producerAncestors.Contains(current))
                continue;
            common = current;
            break;
        }
        if (common is null || !string.Equals(common.Node.Structure?.Kind, SequenceStructureKind, StringComparison.Ordinal))
            return false;

        var producerChild = ChildBelow(producer, common);
        var consumerChild = ChildBelow(consumer, common);
        return !ReferenceEquals(producerChild, consumerChild) && producerChild.Order < consumerChild.Order;
    }

    private static NodeLocation ChildBelow(NodeLocation node, NodeLocation ancestor)
    {
        var current = node;
        while (current.Parent is not null && !ReferenceEquals(current.Parent, ancestor))
            current = current.Parent;
        return current;
    }

    private ExpressionValueShape ToValueShape(
        TypeReference type,
        bool isNullable,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = ToElementShape(type.Alias, isNullable, cancellationToken, depth);
        return type.CollectionKind switch
        {
            CollectionKind.Single => item,
            CollectionKind.Dictionary => new(type.Alias, ExpressionValueKind.Map, isNullable, Item: null, Key: new("String", ExpressionValueKind.Scalar, false), Value: item),
            _ => new(type.Alias, ExpressionValueKind.Array, isNullable, Item: item)
        };
    }

    private ExpressionValueShape ToElementShape(
        string alias,
        bool isNullable,
        CancellationToken cancellationToken,
        int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wellKnownTypes = WellKnownTypes;
        if (wellKnownTypes?.TryGetTypeOrDefault(alias, out var type) != true ||
            IsScalar(type))
            return new(alias, ExpressionValueKind.Scalar, isNullable);
        if (depth >= 4)
            return new(alias, ExpressionValueKind.Object, isNullable, Members: []);

        var members = type
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Take(100)
            .Select(property =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var memberType = TypeReferenceFactory.FromClrType(property.PropertyType, wellKnownTypes.GetAliasOrDefault);
                return new ExpressionValueMember(
                    property.Name,
                    ToValueShape(memberType, IsNullable(property.PropertyType), cancellationToken, depth + 1));
            })
            .ToArray();
        return new(alias, ExpressionValueKind.Object, isNullable, Members: members);
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(Guid) ||
        Nullable.GetUnderlyingType(type) is not null;

    private static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static string PermissionRevision(string? subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
            return "authenticated";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(subjectId));
        return Convert.ToHexString(digest.AsSpan(0, 8));
    }

    private static string ContextRevision(params string[] parts)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    private ActivityNode? FindNode(ActivityNode? root, string nodeId, CancellationToken cancellationToken)
    {
        if (root is null)
            return null;
        var stack = new Stack<ActivityNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        stack.Push(root);
        while (stack.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current.NodeId))
                continue;
            if (string.Equals(current.NodeId, nodeId, StringComparison.Ordinal))
                return current;
            foreach (var child in structures.ProjectChildren(current).SelectMany(projection => projection.Activities))
                stack.Push(child);
        }
        return null;
    }

    private sealed record NodeLocation(ActivityNode Node, NodeLocation? Parent, int Order);
}
