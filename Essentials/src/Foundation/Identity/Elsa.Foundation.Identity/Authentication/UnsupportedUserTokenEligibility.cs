using Elsa.Foundation.Identity.Core.Authentication;

namespace Elsa.Foundation.Identity.Authentication;

/// <summary>
/// Default for hosts whose identity provider cannot re-read a user's current status. It refuses every user, so a
/// refresh token that names an Elsa user fails closed instead of outliving a disabled or deleted account.
/// </summary>
public sealed class UnsupportedUserTokenEligibility : IUserTokenEligibility
{
    public ValueTask<bool> IsEligibleAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}
