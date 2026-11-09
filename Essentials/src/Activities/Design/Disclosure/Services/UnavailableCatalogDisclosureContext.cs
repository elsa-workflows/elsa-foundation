using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Disclosure.Services;

internal sealed class UnavailableCatalogDisclosureAuthority : IActivityCatalogDisclosureAuthority
{
    public bool IsPresent => false;
}

internal sealed class UnavailableCatalogOwnerScopeAccessor : IActivityCatalogOwnerScopeAccessor
{
    public CatalogOwnerScope Current => new(CatalogOwnerScopeKind.Unspecified, null);
}
