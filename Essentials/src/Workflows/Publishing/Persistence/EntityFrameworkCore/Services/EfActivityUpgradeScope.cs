using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// The Activities Design row filters the upgrade bridge needs and the lane's own stores do not expose:
/// exact identity by hashed key with a residual comparison, and the caller's persistence scope.
/// </summary>
/// <remarks>
/// These reproduce <c>EfActivityDesignStores</c>'s own predicates against the same shadow columns, because
/// the upgrade bridge reads and compare-and-swaps the plan, receipt and dependency-projection rows directly
/// inside a shared transaction rather than through that store's write helpers. Keeping them here, rather
/// than widening the Activities Design module's internal surface, keeps the change inside Publishing.
/// </remarks>
internal static class EfActivityUpgradeScope
{
    /// <summary>Rows whose hashed identity matches <paramref name="id"/> and whose raw identity is exactly it.</summary>
    public static IQueryable<T> ById<T>(IQueryable<T> query, string id)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var hash = ActivitiesDesignDbContext.ComputeIdentityHash(id);
        return query.Where(x => EF.Property<string>(x, "IdIdentityHash") == hash && EF.Property<string>(x, "Id") == id);
    }

    /// <summary>Rows whose hashed foreign key matches <paramref name="value"/> and whose raw column is exactly it.</summary>
    public static IQueryable<T> ByReference<T>(IQueryable<T> query, string propertyName, string value)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var hash = ActivitiesDesignDbContext.ComputeIdentityHash(value);
        return query.Where(x =>
            EF.Property<string>(x, propertyName + "IdentityHash") == hash &&
            EF.Property<string>(x, propertyName) == value);
    }

    /// <summary>
    /// The rows <paramref name="access"/> may see: its own scope and the global scope. The raw tenant column
    /// is compared alongside the hashed partition key so a digest collision cannot alias another scope.
    /// </summary>
    public static IQueryable<T> InScope<T>(IQueryable<T> query, IPersistenceAccessContextAccessor? access)
        where T : class
    {
        if (access is null || access.Current.AcrossScopes)
            return query;
        if (access.Current.Scope is { } scope)
        {
            var scopeKey = ActivitiesDesignDbContext.NormalizeTenantKey(scope.Value);
            return query.Where(x =>
                (EF.Property<string>(x, "TenantId") == scope.Value && EF.Property<string>(x, "TenantScopeKey") == scopeKey) ||
                (EF.Property<string>(x, "TenantId") == null && EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey));
        }

        return query.Where(x =>
            EF.Property<string>(x, "TenantId") == null &&
            EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey);
    }
}
