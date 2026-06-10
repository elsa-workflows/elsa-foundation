using Groundwork.Core.Indexing;

namespace Groundwork.Core.Queries;

public sealed record PortableQueryDeclaration(
    string Identity,
    string IndexIdentity,
    IReadOnlySet<PortableQueryOperation> Operations,
    QuerySortSupport SortSupport,
    QueryPagingSupport PagingSupport);

public enum QuerySortSupport
{
    None,
    Ascending,
    Descending,
    Both
}

public enum QueryPagingSupport
{
    None,
    Offset,
    Cursor
}
