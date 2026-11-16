using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Iam;

public sealed class OptionsIdentityEmailUniquenessPolicy(IOptions<FoundationIdentityOptions> options)
    : IIdentityEmailUniquenessPolicy
{
    public bool RequireUniqueEmail => options.Value.RequireUniqueEmail;
}
