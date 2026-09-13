using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services
{

/// <summary>
/// Test-only stand-in for the ASP.NET Identity module's four known in-memory authority descriptors.
/// Keeping the exact public type name lets the EF registration test the compatibility seam without
/// making this persistence test project depend on the ASP.NET Identity host package.
/// </summary>
public sealed class InMemoryIdentityStore : IUserStore, IRoleStore, IExternalIdentityStore, ITenantMembershipStore
{
    public ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<RoleRecord?> IRoleStore.FindAsync(string tenantId, string roleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(
        string tenantId,
        string provider,
        string providerSubject,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<TenantMembershipRecord?> ITenantMembershipStore.FindAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
}

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions
{

/// <summary>Registers the known in-memory descriptor/factory shape for EF composition tests.</summary>
public static class AspNetCoreIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryAuthorityShape(this IServiceCollection services)
    {
        services.AddSingleton<IUserStore, Services.InMemoryIdentityStore>();
        services.AddSingleton<IRoleStore>(provider =>
            (Services.InMemoryIdentityStore)provider.GetRequiredService<IUserStore>());
        services.AddSingleton<IExternalIdentityStore>(provider =>
            (Services.InMemoryIdentityStore)provider.GetRequiredService<IUserStore>());
        services.AddSingleton<ITenantMembershipStore>(provider =>
            (Services.InMemoryIdentityStore)provider.GetRequiredService<IUserStore>());
        return services;
    }
}
}
