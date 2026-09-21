namespace Elsa.Primitives.Persistence;

/// <summary>
/// Represents pagination arguments.
/// </summary>
public sealed record PageArgs
{
    /// <summary>
    /// Gets pagination arguments that returns all items.
    /// </summary>
    public static PageArgs All => new() { Offset = null, Limit = null };

    /// <summary>
    /// Creates pagination arguments from a page number and page size.
    /// </summary>
    /// <param name="page">The zero-based page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    public static PageArgs FromPage(int? page, int? pageSize) => new() { Offset = page * pageSize, Limit = pageSize };

    /// <summary>
    /// Creates pagination arguments from an offset and limit.
    /// </summary>
    /// <param name="offset">The number of items to skip.</param>
    /// <param name="limit">The number of items to take.</param>
    public static PageArgs FromRange(int? offset, int? limit) => new() { Offset = offset, Limit = limit };

    /// <summary>
    /// Creates pagination arguments from page and page size or offset and limit. When both pairs are
    /// complete, the page pair wins. When no value is specified at all, defaults to the first page of 100.
    /// </summary>
    /// <param name="page">The zero-based page number.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <param name="offset">The number of items to skip.</param>
    /// <param name="limit">The number of items to take.</param>
    /// <exception cref="ArgumentException">Thrown when a pair is only partially specified, so that neither page and pageSize nor offset and limit form a complete pair.</exception>
    public static PageArgs From(int? page, int? pageSize, int? offset, int? limit)
    {
        if (page != null && pageSize != null)
            return FromPage(page, pageSize);

        if (offset != null && limit != null)
            return FromRange(offset, limit);

        if (page != null || pageSize != null || offset != null || limit != null)
            throw new ArgumentException("Pagination arguments are ambiguous: specify both page and pageSize, or both offset and limit.");

        return FromPage(0, 100);
    }

    /// <summary>
    /// Gets the offset of the page.
    /// </summary>
    public int? Offset { get; set; }

    /// <summary>
    /// Gets or sets the limit of the page.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Gets the zero-based page number, or <c>null</c> when the arguments do not describe a page
    /// (no offset, or a non-positive limit such as a "count only" request with <c>Limit == 0</c>).
    /// </summary>
    public int? Page => Offset.HasValue && Limit is > 0 ? Offset / Limit : null;

    /// <summary>
    /// Gets the number of items per page.
    /// </summary>
    public int? PageSize => Limit;

    /// <summary>
    /// Returns pagination arguments for the next page.
    /// </summary>
    /// <returns>The arguments for the next page.</returns>
    public PageArgs Next() => this with { Offset = Offset + Limit };
}