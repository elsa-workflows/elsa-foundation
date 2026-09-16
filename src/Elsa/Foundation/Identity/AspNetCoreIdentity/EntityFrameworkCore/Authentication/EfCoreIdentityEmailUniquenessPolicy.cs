using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;

public sealed class EfCoreIdentityEmailUniquenessPolicy(IOptions<IdentityOptions> options) : IIdentityEmailUniquenessPolicy
{
    public bool RequireUniqueEmail => options.Value.User.RequireUniqueEmail;
}
