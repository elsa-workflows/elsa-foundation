namespace Groundwork.Documents.Store;

public sealed record DocumentStoreQuery(
    string DocumentKind,
    string IndexName,
    string Value,
    int? Skip = null,
    int? Take = null);
