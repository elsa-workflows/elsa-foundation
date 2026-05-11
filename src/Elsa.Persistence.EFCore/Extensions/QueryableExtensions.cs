using Elsa.Primitives.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EFCore.Extensions
{
    public static class QueryableExtensions
    {
        public static async Task<Page<T>> PaginateAsync<T>(this IQueryable<T> queryable, PageArgs? pageArgs = null, Func<List<T>, Task>? onMaterialization = null)
        {
            var count = await queryable.CountAsync();
            if (pageArgs?.Offset != null) queryable = queryable.Skip(pageArgs.Offset.Value);
            if (pageArgs?.Limit != null) queryable = queryable.Take(pageArgs.Limit.Value);

            var results = await queryable.ToListAsync();

            if(onMaterialization is not null)
            {
                await onMaterialization.Invoke(results);
            }

            return Page.Of(results, count);
        }
    }
}
