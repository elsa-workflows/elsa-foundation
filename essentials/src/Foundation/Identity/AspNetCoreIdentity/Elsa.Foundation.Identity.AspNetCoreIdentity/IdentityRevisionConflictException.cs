namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

/// <summary>
/// Raised by an ASP.NET Core Identity store when a write lost a revision race to a concurrent writer, for a member that
/// has no <see cref="Microsoft.AspNetCore.Identity.IdentityResult"/> to carry the outcome. Re-reading and retrying is the
/// correct response. Derives from <see cref="InvalidOperationException"/> so callers that already catch that keep working.
/// </summary>
public sealed class IdentityRevisionConflictException(string message) : InvalidOperationException(message);
