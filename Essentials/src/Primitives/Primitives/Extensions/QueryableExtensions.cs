using Elsa.Primitives.Persistence;

namespace Elsa.Primitives.Extensions;

/// <summary>
/// Provides extension methods for <see cref="IQueryable{T}"/>.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Orders the queryable by the specified order.
    /// </summary>
    /// <param name="queryable">The queryable to order.</param>
    /// <param name="order">The order to apply to the queryable.</param>
    /// <typeparam name="T">The type of the queryable.</typeparam>
    /// <typeparam name="TOrderBy">The type of the property to order by.</typeparam>
    /// <returns>The ordered queryable.</returns>
    public static IQueryable<T> OrderBy<T, TOrderBy>(this IQueryable<T> queryable, OrderDefinition<T, TOrderBy> order) =>
        order.Direction == OrderDirection.Ascending
            ? queryable.OrderBy(order.KeySelector)
            : queryable.OrderByDescending(order.KeySelector);

    /// <summary>
    /// Paginates the queryable.
    /// </summary>
    /// <param name="queryable">The queryable to paginate.</param>
    /// <param name="pageArgs">The pagination arguments.</param>
    /// <typeparam name="T">The type of the queryable.</typeparam>
    /// <returns>The paginated queryable.</returns>
    public static IQueryable<T> Paginate<T>(this IQueryable<T> queryable, PageArgs? pageArgs)
    {
        if (pageArgs?.Offset != null) queryable = queryable.Skip(pageArgs.Offset.Value);
        if (pageArgs?.Limit != null) queryable = queryable.Take(pageArgs.Limit.Value);

        return queryable;
    }
}