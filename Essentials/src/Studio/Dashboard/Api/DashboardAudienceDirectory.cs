using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Studio.Dashboard.Core.Resources;

namespace Elsa.Studio.Dashboard.Api;

internal sealed class DashboardAudienceDirectory(DashboardAudienceCursors cursors, IUserStore? users = null,
    IRoleStore? roles = null, IPagedUserStore? pagedUsers = null, IPagedRoleStore? pagedRoles = null) : IDashboardAudienceDirectory
{
    public async ValueTask<bool> IsValidAsync(DashboardActor actor, DashboardAudienceGrant grant, CancellationToken cancellationToken = default)
    {
        if (grant.Kind == "user" && Allowed(actor, "identity/users:read") && users is not null)
            return await users.FindAsync(actor.TenantId, grant.Id, cancellationToken) is { Status: UserStatus.Active } user &&
                user.Id == grant.Id && user.TenantId == actor.TenantId;
        if (grant.Kind == "role" && Allowed(actor, "identity/roles:read") && roles is not null)
            return await roles.FindAsync(actor.TenantId, grant.Id, cancellationToken) is { } role &&
                role.Id == grant.Id && role.TenantId == actor.TenantId;
        return false;
    }

    public async ValueTask<DashboardAudienceCandidatePage> SearchAsync(DashboardActor actor, string kind, string? search,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        if (search?.Length > 200) throw new DashboardResourceException("validation", "Audience search exceeds 200 characters.");
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var scope = DashboardAudienceCursors.Scope(actor, kind, search);
        var skip = continuationToken is null ? 0 : cursors.Read(continuationToken, scope);
        if (kind == "user")
        {
            if (!Allowed(actor, "identity/users:read")) throw Denied();
            if (pagedUsers is null) throw Unavailable();
            var page = await pagedUsers.ListPageAsync(actor.TenantId, new(skip, 100), search, cancellationToken);
            EnsureProgress(skip, page.Items.Count, page.TotalCount);
            var next = skip + page.Items.Count;
            return new(page.Items.Where(user => user.TenantId == actor.TenantId && user.Status == UserStatus.Active).Select(user =>
                new DashboardAudienceCandidate("user", user.Id, user.DisplayName ?? user.UserName)).ToArray(),
                next < page.TotalCount ? cursors.Write(scope, next) : null);
        }
        if (kind == "role")
        {
            if (!Allowed(actor, "identity/roles:read")) throw Denied();
            if (pagedRoles is null) throw Unavailable();
            var page = await pagedRoles.ListPageAsync(actor.TenantId, new(skip, 100), cancellationToken);
            EnsureProgress(skip, page.Items.Count, page.TotalCount);
            var next = skip + page.Items.Count;
            return new(page.Items.Where(role => role.TenantId == actor.TenantId &&
                    (search is null || role.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .Select(role => new DashboardAudienceCandidate("role", role.Id, role.Name)).ToArray(),
                next < page.TotalCount ? cursors.Write(scope, next) : null);
        }
        throw new DashboardResourceException("validation", "Choose user or role audience discovery.");
    }

    private static void EnsureProgress(int skip, int count, long total)
    {
        if (count == 0 && skip < total) throw new DashboardResourceException("unavailable", "The identity directory did not advance its page. Retry the search.");
    }
    private static bool Allowed(DashboardActor actor, string permission) => actor.Permissions.Contains(permission) || actor.Permissions.Contains("*");
    private static DashboardResourceException Denied() => new("denied", "Audience discovery requires the corresponding identity directory read permission.");
    private static DashboardResourceException Unavailable() => new("unavailable", "This identity provider does not support bounded discovery for the requested audience.");
}

internal sealed class DashboardAudienceCursors
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    public static string Scope(DashboardActor actor, string kind, string? search) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { actor.TenantId, actor.UserId, kind, search ?? "" })));

    public string Write(string scope, int skip)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Cursor(scope, skip));
        return Convert.ToBase64String(payload) + "." + Convert.ToBase64String(HMACSHA256.HashData(key, payload));
    }

    public int Read(string token, string scope)
    {
        try
        {
            if (token.Length > 1_024) throw Invalid();
            var parts = token.Split('.');
            if (parts.Length != 2) throw Invalid();
            var payload = Convert.FromBase64String(parts[0]);
            var signature = Convert.FromBase64String(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, payload), signature)) throw Invalid();
            var cursor = JsonSerializer.Deserialize<Cursor>(payload);
            if (cursor is null || cursor.Scope != scope || cursor.Skip < 0) throw Invalid();
            return cursor.Skip;
        }
        catch (Exception error) when (error is FormatException or JsonException) { throw Invalid(); }
    }

    private static DashboardResourceException Invalid() => new("validation", "The audience continuation is invalid or expired. Restart the search.");
    private sealed record Cursor(string Scope, int Skip);
}
