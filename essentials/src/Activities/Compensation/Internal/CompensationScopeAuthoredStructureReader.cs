using System.Text.Json;
using Elsa.Activities.Compensation.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Reads a <c>CompensationScope</c> node's authored structure from its <see cref="ActivityNode.Structure"/> payload.
/// Shared by the structure handler and the Draft validator so authored-payload deserialization lives in one place.
/// </summary>
internal static class CompensationScopeAuthoredStructureReader
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums arrive as camelCase strings from the global web JSON options;
        // nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static CompensationScopeAuthoredStructure Read(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new CompensationScopeAuthoredStructure();

        return activity.Structure.Payload.Deserialize<CompensationScopeAuthoredStructure>(SerializerOptions)
               ?? new CompensationScopeAuthoredStructure();
    }
}
