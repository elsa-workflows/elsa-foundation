using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Elsa.Primitives.Extensions
{
    /// <summary>
    /// Provides extension methods for <see cref="VersionedEntity"/> objects.
    /// </summary>
    public static class VersionedEntityExtensions
    {
        public static IQueryable<T> WithVersion<T>(this IQueryable<T> query, VersionOptions versionOptions)
            where T : VersionedEntity
        {
            if (versionOptions.IsDraft)
                return query.Where(x => !x.IsPublished);
            if (versionOptions.IsLatest)
                return query.Where(x => x.IsLatest);
            if (versionOptions.IsPublished)
                return query.Where(x => x.IsPublished);
            if (versionOptions.IsLatestOrPublished)
                return query.Where(x => x.IsPublished || x.IsLatest);
            if (versionOptions.IsLatestAndPublished)
                return query.Where(x => x.IsPublished && x.IsLatest);
            if (versionOptions.Version > 0)
                return query.Where(x => x.Version == versionOptions.Version);

            return query;
        }

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
}
