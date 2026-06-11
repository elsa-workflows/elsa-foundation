namespace Groundwork.Documents.Store;

public sealed record DocumentStoreQuery
{
    public DocumentStoreQuery(string documentKind, string indexName, string value, int? skip = null, int? take = null)
    {
        if (skip is < 0)
            throw new ArgumentOutOfRangeException(nameof(skip), skip, "Skip must be greater than or equal to 0.");

        if (take is < 0)
            throw new ArgumentOutOfRangeException(nameof(take), take, "Take must be greater than or equal to 0.");

        DocumentKind = documentKind;
        IndexName = indexName;
        Value = value;
        Skip = skip;
        Take = take;
    }

    public string DocumentKind { get; }
    public string IndexName { get; }
    public string Value { get; }
    public int? Skip { get; }
    public int? Take { get; }
}
