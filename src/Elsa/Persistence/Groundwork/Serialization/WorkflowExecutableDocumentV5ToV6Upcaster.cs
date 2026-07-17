using System.Text.Json.Nodes;
using Groundwork.Documents.Serialization;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Advances workflow executables to the format that permits explicit activity-input nullability.
/// </summary>
/// <remarks>
/// Version 5 contracts predate input nullability. Missing <c>isNullable</c> members intentionally remain
/// absent so <c>ActivityInputContract.IsNullable</c> deserializes as unknown and its captured schema
/// fingerprint remains valid. The upcaster must not infer nullability from mutable CLR or Design state.
/// </remarks>
public sealed class WorkflowExecutableDocumentV5ToV6Upcaster : IDocumentJsonUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind;
    public int FromVersion => 5;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content;
    }
}
