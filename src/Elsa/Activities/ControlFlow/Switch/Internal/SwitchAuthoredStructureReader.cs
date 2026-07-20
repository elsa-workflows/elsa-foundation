using System.Text.Json;
using Elsa.Activities.Switch.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Switch.Internal;

/// <summary>
/// Reads a <c>Switch</c> node's authored design-time structure from its <see cref="ActivityNode.Structure"/>
/// payload. Shared by the structure handler and the duplicate-case validator so authored-payload
/// deserialization lives in one place.
/// </summary>
internal static class SwitchAuthoredStructureReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Authored ArgumentState.Conversion enums (AuthoredValueConversionMode) arrive as camelCase
        // strings from the global FastEndpoints options; nested structure payload reads must match.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static SwitchAuthoredStructure Read(ActivityNode activity)
    {
        if (activity.Structure is null)
            return new SwitchAuthoredStructure();

        return activity.Structure.Payload.Deserialize<SwitchAuthoredStructure>(SerializerOptions)
               ?? new SwitchAuthoredStructure();
    }
}
