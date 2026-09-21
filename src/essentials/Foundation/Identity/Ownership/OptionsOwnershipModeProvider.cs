using Elsa.Foundation.Identity.Core.Ownership;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Ownership;

public sealed class OptionsOwnershipModeProvider(IOptions<FoundationIdentityOptions> options) : IOwnershipModeProvider
{
    public ValueTask<OwnershipConfiguration> GetAsync(string? tenantId = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(OwnershipConfiguration.FromMode(options.Value.OwnershipMode, options.Value.PermissionPropagation));
}
