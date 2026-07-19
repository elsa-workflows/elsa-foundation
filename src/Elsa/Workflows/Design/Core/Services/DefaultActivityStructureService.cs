using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Services;

public sealed class DefaultActivityStructureService : IActivityStructureService
{
    private readonly IReadOnlyDictionary<StructureKey, IActivityStructureHandler> _handlers;

    public DefaultActivityStructureService(IEnumerable<IActivityStructureHandler> handlers)
    {
        _handlers = handlers.ToDictionary(
            handler => new StructureKey(handler.Kind, handler.SchemaVersion),
            handler => handler);
    }

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return TryGetHandler(activity.Structure, out var handler)
            ? handler.ProjectChildren(activity)
            : [];
    }

    public IReadOnlyCollection<ActivityChildContractMemberUsage> ProjectChildContractMemberUsage(ActivityNode activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return TryGetHandler(activity.Structure, out var handler)
            ? handler.ProjectChildContractMemberUsage(activity)
            : [];
    }

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(childProjections);

        if (TryGetHandler(activity.Structure, out var handler))
            return handler.ReplaceChildren(activity, childProjections);

        if (childProjections.Count == 0)
            return activity;

        var structureKind = activity.Structure?.Kind ?? "<none>";
        throw new InvalidOperationException($"Activity node '{activity.NodeId}' has no structure handler for '{structureKind}'.");
    }

    public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Structure is null)
            return null;

        return TryGetHandler(activity.Structure, out var handler)
            ? handler.CompileExecutableStructure(activity)
            : new ActivityNodeStructure(activity.Structure.Kind, activity.Structure.SchemaVersion, activity.Structure.Payload);
    }

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return TryGetHandler(activity.Structure, out var handler)
            ? handler.ProjectScopedVariables(activity)
            : [];
    }

    public bool SupportsScopedVariables(ActivityNode activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return TryGetHandler(activity.Structure, out var handler) && handler.SupportsScopedVariables;
    }

    private bool TryGetHandler(ActivityNodeStructure? structure, out IActivityStructureHandler handler)
    {
        if (structure is not null && _handlers.TryGetValue(new StructureKey(structure.Kind, structure.SchemaVersion), out handler!))
            return true;

        handler = null!;
        return false;
    }

    private readonly record struct StructureKey(string Kind, string SchemaVersion);
}
