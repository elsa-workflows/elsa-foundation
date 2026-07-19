using System.Security.Claims;
using System.Text.RegularExpressions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;

namespace Elsa.Studio.Preferences.Api.Services;

public sealed partial class StudioPreferenceScopeResolver(IAuthSessionService sessions)
{
    public const string StudioHostIdHeader = "X-Elsa-Studio-Host-Id";

    public async ValueTask<StudioPreferenceKey> ResolveAsync(
        ClaimsPrincipal principal,
        string? studioHostId,
        string @namespace,
        CancellationToken cancellationToken = default)
    {
        var session = await sessions.GetAsync(principal, cancellationToken);
        if (!string.Equals(session.Status, "authenticated", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(session.Subject) ||
            string.IsNullOrWhiteSpace(session.TenantId))
            throw new StudioPreferenceScopeException("An authenticated subject and tenant are required.");

        if (string.IsNullOrWhiteSpace(studioHostId) || studioHostId.Length > 128 || !HostIdPattern().IsMatch(studioHostId))
            throw new StudioPreferenceHostException("X-Elsa-Studio-Host-Id must contain 1-128 letters, digits, dots, underscores, colons, or hyphens.");

        return new(session.Subject, session.TenantId, studioHostId, @namespace);
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostIdPattern();
}
