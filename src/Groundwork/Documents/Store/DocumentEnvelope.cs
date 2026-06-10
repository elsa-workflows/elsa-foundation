using System.Text.Json;

namespace Groundwork.Documents.Store;

public sealed record DocumentEnvelope(
    string DocumentKind,
    string Id,
    string SchemaVersion,
    long Version,
    JsonDocument Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string ContentJson => Content.RootElement.GetRawText();
}
