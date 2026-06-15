using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Shared serialization settings for runtime documents persisted through the Groundwork bridge.
/// Web defaults emit camelCase property names, so the declared keyword index fields (for example
/// <c>workflowExecutionId</c>) match the serialized JSON the relational/document providers index.
/// </summary>
public static class GroundworkRuntimeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
