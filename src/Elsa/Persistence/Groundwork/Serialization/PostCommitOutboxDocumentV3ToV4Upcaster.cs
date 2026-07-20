using System.Text.Json.Nodes;
using Groundwork.Documents.Serialization;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Admits v3 post-commit outbox documents into the v4 envelope, whose optional logical identity
/// remains absent until an overlong identity is next written through the current bridge.
/// </summary>
public sealed class PostCommitOutboxDocumentV3ToV4Upcaster : IDocumentJsonUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind;
    public int FromVersion => 3;

    public JsonObject Upcast(JsonObject content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content;
    }
}
