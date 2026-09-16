using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>
/// Re-checks EF authority ownership when options validation runs at startup. The service collection
/// remains mutable until the host is built, so this catches a later unowned store descriptor that
/// could otherwise silently override the EF registration after feature composition.
/// </summary>
public sealed class IdentityAuthorityStoreRegistrationValidator(
    IServiceCollection services,
    IdentityAuthorityStoreBackend backend) : IValidateOptions<FoundationIdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, FoundationIdentityOptions options)
    {
        try
        {
            var marker = services
                .Where(descriptor => descriptor.ServiceType == typeof(IdentityAuthorityStoreBackend))
                .Select(descriptor => descriptor.ImplementationInstance)
                .OfType<IdentityAuthorityStoreBackend>()
                .SingleOrDefault();
            if (!ReferenceEquals(marker, backend))
                return ValidateOptionsResult.Fail("Identity EF authority backend ownership marker was replaced or duplicated after registration.");

            backend.EnsureOwnsRegisteredContracts(services);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
