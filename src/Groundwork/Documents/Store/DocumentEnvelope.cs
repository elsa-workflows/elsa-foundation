namespace Groundwork.Documents.Store;

public sealed record DocumentEnvelope(
    string DocumentKind,
    string Id,
    string SchemaVersion,
    long Version,
    string ContentJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
