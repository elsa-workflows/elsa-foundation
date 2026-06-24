using System.Text.Json;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>
/// Shared JSON serialization settings for workflow-design Groundwork documents. Web (camelCase) defaults
/// so serialized field paths match the declared index field names (e.g. <c>collection</c>) — the same
/// convention the runtime bridge's <c>GroundworkRuntimeJson</c> follows.
/// <para>
/// Simple aggregates (e.g. <c>WorkflowDefinition</c>) serialize directly with these defaults. The rich
/// design entities (<c>WorkflowDefinitionVersion</c>, <c>WorkflowDefinitionDraft</c>,
/// <c>WorkflowDefinitionVersionLayout</c>) require the <b>domain-projection</b> model recorded in the
/// design-provider implementation plan — serialize the logical <c>State</c>/<c>Records</c> directly, exclude
/// the EF shadow <c>*Source</c> strings and navigation properties — which a per-type
/// <see cref="System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver"/> modifier on these
/// options will apply when those adapters are added.
/// </para>
/// </summary>
public static class GroundworkDesignJson
{
    /// <summary>The shared options instance used by every workflow-design Groundwork document store.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
