using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;

/// <summary>Optional, private consumer setup through the actual Identity persistence authority.</summary>
internal static class QualificationPrincipals
{
    // Qualification:Principals is an array of subject, tenant, username, password and
    // exactDirectPermissions. Neither credentials nor configuration values are logged.
    internal static async Task InitializeAsync(IShellRegistry registry, string shellName,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var entries = ReadEntries(configuration);
        if (entries.Length == 0) return;
        var shell = await registry.GetOrActivateAsync(shellName, cancellationToken);
        foreach (var tenant in entries.GroupBy(entry => entry.Tenant, StringComparer.Ordinal))
        {
            // A partition is selected before resolving either Identity authority. No ambient
            // HTTP principal or request field supplies the bootstrap's persistence context.
            await using var scope = shell.BeginScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope(tenant.Key)));
            var passwords = services.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
            var users = services.GetRequiredService<IRevisionAwareUserStore>();
            var memberships = services.GetRequiredService<ITenantMembershipStore>();
            foreach (var entry in tenant)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var user = await passwords.FindByIdAsync(entry.Subject);
                if (user is null)
                {
                    if (await passwords.FindByNameAsync(entry.Username) is not null)
                        throw Failure(entry, "the configured username already belongs to another subject");
                    user = new AspNetCoreIdentityUser { Id = entry.Subject, TenantId = entry.Tenant, UserName = entry.Username };
                    var created = await passwords.CreateAsync(user, entry.Password);
                    if (!created.Succeeded)
                        throw Failure(entry, "native password-user creation failed (" + string.Join(",", created.Errors.Select(error => error.Code)) + ")");
                }
                if (user.Id != entry.Subject || user.TenantId != entry.Tenant || user.UserName != entry.Username ||
                    !await passwords.CheckPasswordAsync(user, entry.Password))
                    throw Failure(entry, "the existing native password identity does not match its configuration");

                var stored = await users.FindWithRevisionAsync(entry.Tenant, entry.Subject, cancellationToken)
                    ?? throw Failure(entry, "the native user has no provider-neutral persisted record");
                if (stored.Record.Id != entry.Subject || stored.Record.TenantId != entry.Tenant || stored.Record.UserName != entry.Username ||
                    stored.Record.Status != UserStatus.Active || stored.Record.RoleIds.Count != 0)
                    throw Failure(entry, "an existing disabled or role-bearing user cannot become a qualification principal");
                var membership = await memberships.FindAsync(entry.Tenant, entry.Subject, cancellationToken);
                if (membership is not null && (membership.RoleIds.Count != 0 || membership.DirectPermissions.Count != 0))
                    throw Failure(entry, "tenant membership would add permissions outside the exact direct grants");

                var desired = stored.Record with { DirectPermissions = entry.Permissions, RoleIds = new HashSet<string>(StringComparer.Ordinal) };
                if (!stored.Record.DirectPermissions.SetEquals(entry.Permissions))
                {
                    var saved = await users.SaveWithRevisionAsync(desired, stored.Revision, cancellationToken);
                    if (saved.Status != IamRevisionSaveStatus.Saved)
                        throw Failure(entry, "the native user revision changed during exact-grant setup");
                }
                var observed = await users.FindWithRevisionAsync(entry.Tenant, entry.Subject, cancellationToken);
                if (observed is null || observed.Record.Id != entry.Subject || observed.Record.TenantId != entry.Tenant ||
                    observed.Record.UserName != entry.Username || observed.Record.Status != UserStatus.Active || observed.Record.RoleIds.Count != 0 ||
                    !observed.Record.DirectPermissions.SetEquals(entry.Permissions))
                    throw Failure(entry, "the owning Identity store did not confirm the exact configured grants");
            }
        }
    }

    internal static void MapEndpoints(IEndpointRouteBuilder endpoints, IShellRegistry registry, string shellName, IConfiguration configuration)
    {
        // The request selects only these startup configuration snapshots. It cannot name a
        // subject, tenant or permission, and configuration reload cannot expand the selection.
        var entries = ReadEntries(configuration);
        var profiles = ReadProfiles(configuration);
        const string route = "/__qualification/principals/{index:int}";
        endpoints.MapGet(route + "/authority", (int index, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return AuthorityAsync(registry, shellName, entries, profiles, index, null, ct);
            })
            .RequirePermission(ModuleManagementPermissionKeys.Read);
        endpoints.MapPost(route + "/grant-profile", (int index, HttpContext http, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return AuthorityAsync(registry, shellName, entries, profiles, index, http.Request, ct);
            })
            .RequirePermission(ModuleManagementPermissionKeys.Manage);
    }

    private static async Task<IResult> AuthorityAsync(IShellRegistry registry, string shellName, Entry[] entries,
        IReadOnlyDictionary<string, IReadOnlySet<string>> profiles, int index, HttpRequest? request, CancellationToken ct)
    {
        try
        {
            if (index < 0 || index >= entries.Length)
                throw new AuthorityRefusal(404, "qualification.principal.not_configured", "The principal index is not configured.");
            var entry = entries[index];
            string? selectedProfile = null;
            string? revision = null;
            if (request is not null)
            {
                if (!request.HasJsonContentType())
                    throw new AuthorityRefusal(415, "qualification.principal.request_invalid", "A JSON profile selection is required.");
                using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                var value = body.RootElement;
                if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2 ||
                    !value.TryGetProperty("profile", out var profile) || profile.ValueKind != JsonValueKind.String ||
                    !value.TryGetProperty("revision", out var expected) || expected.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(profile.GetString()) || string.IsNullOrWhiteSpace(expected.GetString()))
                    throw new AuthorityRefusal(400, "qualification.principal.request_invalid", "Exactly profile and the observed IAM revision are required.");
                selectedProfile = profile.GetString()!;
                revision = expected.GetString()!;
                if (!profiles.ContainsKey(selectedProfile))
                    throw new AuthorityRefusal(400, "qualification.principal.profile_unavailable", "The grant profile is not predeclared.");
            }

            var shell = await registry.GetOrActivateAsync(shellName, ct);
            await using var scope = shell.BeginScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope(entry.Tenant)));
            var users = services.GetRequiredService<IRevisionAwareUserStore>();
            var observed = await ObserveAsync(services, entry, ct);
            if (selectedProfile is not null)
            {
                var grants = profiles[selectedProfile];
                var saved = await users.SaveWithRevisionAsync(observed.Record with { DirectPermissions = grants }, revision, ct);
                if (saved.Status == IamRevisionSaveStatus.NotFound)
                    throw new AuthorityRefusal(404, "qualification.principal.not_found", "The configured principal is absent from its owning Identity store.");
                if (saved.Status != IamRevisionSaveStatus.Saved)
                    throw new AuthorityRefusal(409, "qualification.principal.revision_conflict", "The native Identity store did not accept the supplied IAM revision.");
                observed = await ObserveAsync(services, entry, ct);
                if (string.IsNullOrWhiteSpace(saved.Revision) || observed.Revision != saved.Revision || !observed.Record.DirectPermissions.SetEquals(grants))
                    throw new AuthorityRefusal(409, "qualification.principal.confirmation_changed", "The owning Identity store did not confirm the exact selected grants and saved revision.");
            }
            return Results.Json(new { index, subject = observed.Record.Id, tenant = observed.Record.TenantId,
                username = observed.Record.UserName, revision = observed.Revision, status = observed.Record.Status.ToString(),
                directPermissions = observed.Record.DirectPermissions.Order(StringComparer.Ordinal).ToArray(),
                roleIds = observed.Record.RoleIds.Order(StringComparer.Ordinal).ToArray(),
                availableProfiles = profiles.Keys.Order(StringComparer.Ordinal).ToArray(), selectedProfile });
        }
        catch (JsonException)
        {
            return Results.Problem(statusCode: 400, title: "qualification.principal.request_invalid", detail: "A valid JSON profile selection is required.");
        }
        catch (AuthorityRefusal refusal)
        {
            return Results.Problem(statusCode: refusal.Status, title: refusal.Code, detail: refusal.Message);
        }
    }

    private static async Task<IamRevisionedRecord<UserRecord>> ObserveAsync(IServiceProvider services, Entry entry, CancellationToken ct)
    {
        var observed = await services.GetRequiredService<IRevisionAwareUserStore>().FindWithRevisionAsync(entry.Tenant, entry.Subject, ct)
            ?? throw new AuthorityRefusal(404, "qualification.principal.not_found", "The configured principal is absent from its owning Identity store.");
        var record = observed.Record;
        var membership = await services.GetRequiredService<ITenantMembershipStore>().FindAsync(entry.Tenant, entry.Subject, ct);
        if (record.Id != entry.Subject || record.TenantId != entry.Tenant || record.UserName != entry.Username ||
            record.Status != UserStatus.Active || record.RoleIds.Count != 0 ||
            membership is not null && (membership.RoleIds.Count != 0 || membership.DirectPermissions.Count != 0))
            throw new AuthorityRefusal(409, "qualification.principal.authority_invalid", "The configured active identity must have no role or membership grants.");
        return observed;
    }

    private static Entry[] ReadEntries(IConfiguration configuration)
    {
        var principals = configuration.GetSection("Qualification:Principals");
        if (principals.Value is not null)
            throw new InvalidOperationException("Qualification:Principals requires an array of private principal entries.");
        var entries = principals.GetChildren().Select((section, index) => Read(section, index)).ToArray();
        if (entries.GroupBy(entry => (entry.Tenant, entry.Subject)).Any(group => group.Count() != 1) ||
            entries.GroupBy(entry => (entry.Tenant, Name: entry.Username.ToUpperInvariant())).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Qualification principals contain duplicate tenant identities.");
        return entries;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ReadProfiles(IConfiguration configuration)
    {
        var section = configuration.GetSection("Qualification:GrantProfiles");
        if (section.Value is not null)
            throw new InvalidOperationException("Qualification:GrantProfiles requires predeclared names mapped to concrete permission arrays.");
        return section.GetChildren().ToDictionary(profile => profile.Key,
            profile => ReadPermissions(profile, "Qualification grant profile"), StringComparer.Ordinal);
    }

    private static Entry Read(IConfigurationSection section, int index)
    {
        var keys = section.GetChildren().Select(child => child.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!keys.SetEquals(["subject", "tenant", "username", "password", "exactDirectPermissions"]))
            throw new InvalidOperationException($"Qualification principal {index} requires exactly subject, tenant, username, password and exactDirectPermissions.");
        string Required(string key)
        {
            var value = section[key];
            if (string.IsNullOrWhiteSpace(value) || key != "password" && value != value.Trim())
                throw new InvalidOperationException($"Qualification principal {index} has a missing or invalid {key}.");
            return value;
        }
        return new(index, Required("subject"), Required("tenant"), Required("username"), Required("password"),
            ReadPermissions(section.GetSection("exactDirectPermissions"), $"Qualification principal {index}"));
    }

    private static IReadOnlySet<string> ReadPermissions(IConfigurationSection permissionSection, string owner)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var canonical = new HashSet<string>(StringComparer.Ordinal);
        if (permissionSection.Value is not null)
            throw new InvalidOperationException(owner + " requires a concrete permission array.");
        var permissionIndex = 0;
        foreach (var permission in permissionSection.GetChildren())
        {
            var value = permission.Value;
            if (permission.Key != (permissionIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                value is null || !PermissionKey.IsConcrete(value) || !canonical.Add(PermissionKey.Normalize(value)))
                throw new InvalidOperationException(owner + " requires unique, concrete direct permissions without wildcards.");
            permissions.Add(value);
        }
        return permissions;
    }

    private static InvalidOperationException Failure(Entry entry, string reason) =>
        new($"Qualification principal {entry.Index}: {reason}.");

    private sealed class AuthorityRefusal(int status, string code, string message) : Exception(message)
    {
        internal int Status { get; } = status;
        internal string Code { get; } = code;
    }

    private sealed record Entry(int Index, string Subject, string Tenant, string Username, string Password, IReadOnlySet<string> Permissions);
}
