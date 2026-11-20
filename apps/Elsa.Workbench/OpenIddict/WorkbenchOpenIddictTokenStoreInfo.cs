using Elsa.Foundation.Identity.OpenIddict;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench.OpenIddict;

/// <summary>
/// Answers <see cref="IOpenIddictTokenStoreInfo"/> for the Workbench-owned EF Core OpenIddict store: the store is
/// in memory when a registered <see cref="DbContext"/> uses a non-relational provider, which is the InMemory provider
/// of Workbench development/demo mode.
/// </summary>
internal sealed class WorkbenchOpenIddictTokenStoreInfo(IServiceProvider services) : IOpenIddictTokenStoreInfo
{
    public bool IsInMemory => services.GetServices<DbContext>().Any(context => !context.Database.IsRelational());
}
