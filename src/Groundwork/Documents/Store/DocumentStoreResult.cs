namespace Groundwork.Documents.Store;

public sealed record SaveDocumentRequest(
    string DocumentKind,
    string Id,
    string SchemaVersion,
    string ContentJson,
    long? ExpectedVersion = null);

public sealed record DeleteDocumentRequest(
    string DocumentKind,
    string Id,
    long? ExpectedVersion = null);

public sealed record DocumentStoreWriteResult(DocumentStoreWriteStatus Status, DocumentEnvelope? Document = null)
{
    public static DocumentStoreWriteResult Saved(DocumentEnvelope document) => new(DocumentStoreWriteStatus.Saved, document);
    public static DocumentStoreWriteResult Deleted { get; } = new(DocumentStoreWriteStatus.Deleted);
    public static DocumentStoreWriteResult NotFound { get; } = new(DocumentStoreWriteStatus.NotFound);
    public static DocumentStoreWriteResult ConcurrencyConflict { get; } = new(DocumentStoreWriteStatus.ConcurrencyConflict);
}

public enum DocumentStoreWriteStatus
{
    Saved,
    Deleted,
    NotFound,
    ConcurrencyConflict
}

public sealed class UndeclaredDocumentIndexException(string documentKind, string indexName)
    : InvalidOperationException($"Index '{indexName}' is not declared or is not queryable for document kind '{documentKind}'.")
{
    public string DocumentKind { get; } = documentKind;
    public string IndexName { get; } = indexName;
}
