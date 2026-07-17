using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using ElsaExternalIdentityStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkExternalIdentityStore;
using ElsaRoleStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkRoleStore;
using ElsaTenantMembershipStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkTenantMembershipStore;
using ElsaUserStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkUserStore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

internal sealed class AspNetCoreIdentityGroundworkStoreFixture(
    string tenantId,
    InMemoryDocumentStore? documents = null,
    bool requireUniqueEmail = false)
{
    private readonly FixedAccessContextAccessor _accessor = new(
        PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));

    public InMemoryDocumentStore Documents { get; } = documents ?? new InMemoryDocumentStore(IdentityStorageManifest.Create());

    public GroundworkIdentityUserStore UserStore() => new(
        Documents,
        _accessor,
        Documents,
        emailUniquenessPolicy: new IdentityEmailUniquenessPolicy(requireUniqueEmail));

    public GroundworkIdentityRoleStore RoleStore() => new(Documents, _accessor, Documents);

    public ElsaUserStore ElsaUserStore() => new(
        Documents,
        _accessor,
        Documents,
        emailUniquenessPolicy: new IdentityEmailUniquenessPolicy(requireUniqueEmail));

    public ElsaRoleStore ElsaRoleStore() => new(Documents, _accessor, Documents);

    public ElsaExternalIdentityStore ElsaExternalIdentityStore() => new(Documents, _accessor, Documents);

    public ElsaTenantMembershipStore ElsaTenantMembershipStore() => new(Documents, _accessor);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
