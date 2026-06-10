namespace Groundwork.Documents.Store;

public sealed record DocumentStoreQuery
{
    public DocumentStoreQuery(string documentKind, string indexName, string value, int? skip = null, int? take = null)
    {
        if (skip is < 0)
            throw new ArgumentOutOfRangeException(nameof(Skip), skip, "Skip must be greater than or equal to 0.");

        if (take is < 0)
            throw new ArgumentOutOfRangeException(nameof(Take), take, "Take must be greater than or equal to 0.");

        DocumentKind = documentKind;
        IndexName = indexName;
        Value = value;
        Skip = skip;
        Take = take;
    }

    public string DocumentKind { get; init; }
    public string IndexName { get; init; }
    public string Value { get; init; }
    public int? Skip { get; init; }
    public int? Take { get; init; }
}
