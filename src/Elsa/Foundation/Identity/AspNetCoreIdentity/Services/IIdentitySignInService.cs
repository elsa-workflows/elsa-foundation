using Elsa.Foundation.Identity.Abstractions.Authentication;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// First-party password sign-in over ASP.NET Core Identity. Validates credentials, issues the Identity
/// cookie, and returns the resulting <see cref="AuthSession"/>.
/// </summary>
public interface IIdentitySignInService
{
    /// <summary>
    /// Validates <paramref name="username"/>/<paramref name="password"/> (optionally scoped to
    /// <paramref name="tenantId"/>), signs the user in on the cookie scheme, and returns the session, or a
    /// failed result when the credentials are invalid.
    /// </summary>
    ValueTask<SignInOutcome> PasswordSignInAsync(string username, string password, string? tenantId, CancellationToken cancellationToken = default);
}

/// <summary>The result of a first-party password sign-in attempt.</summary>
public sealed record SignInOutcome(bool Succeeded, AuthSession? Session)
{
    public static SignInOutcome Failure { get; } = new(false, null);

    public static SignInOutcome Success(AuthSession session) => new(true, session);
}
