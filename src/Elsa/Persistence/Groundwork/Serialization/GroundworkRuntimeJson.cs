using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Shared serialization settings for runtime documents persisted through the Groundwork bridge.
/// Web defaults emit camelCase property names, so the declared keyword index fields (for example
/// <c>workflowExecutionId</c>) match the serialized JSON the relational/document providers index.
/// </summary>
public static class GroundworkRuntimeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DropDerivedExecutableProjections }
        }
    };

    // WorkflowExecutable recomputes Nodes and NodesById from RootActivity in its constructor, so both
    // are derived projections of the same tree. Persisting them would store the executable graph three
    // times; dropping them keeps the stored document compact and the constructor rebuilds them on load.
    private static void DropDerivedExecutableProjections(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(WorkflowExecutable))
            return;

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var memberName = (typeInfo.Properties[i].AttributeProvider as MemberInfo)?.Name;
            if (memberName is nameof(WorkflowExecutable.Nodes) or nameof(WorkflowExecutable.NodesById))
                typeInfo.Properties.RemoveAt(i);
        }
    }
}
