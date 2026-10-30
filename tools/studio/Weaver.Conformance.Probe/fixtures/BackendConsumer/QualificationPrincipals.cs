using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
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
        var principals = configuration.GetSection("Qualification:Principals");
        if (principals.Value is not null)
            throw new InvalidOperationException("Qualification:Principals requires an array of private principal entries.");
        var entries = principals.GetChildren()
            .Select((section, index) => Read(section, index)).ToArray();
        if (entries.Length == 0) return;
        if (entries.GroupBy(entry => (entry.Tenant, entry.Subject)).Any(group => group.Count() != 1) ||
            entries.GroupBy(entry => (entry.Tenant, Name: entry.Username.ToUpperInvariant())).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Qualification principals contain duplicate tenant identities.");

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
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var canonical = new HashSet<string>(StringComparer.Ordinal);
        var permissionSection = section.GetSection("exactDirectPermissions");
        if (permissionSection.Value is not null)
            throw new InvalidOperationException($"Qualification principal {index} requires an array of exactDirectPermissions.");
        var permissionIndex = 0;
        foreach (var permission in permissionSection.GetChildren())
        {
            var value = permission.Value;
            if (permission.Key != (permissionIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                value is null || !PermissionKey.IsConcrete(value) || !canonical.Add(PermissionKey.Normalize(value)))
                throw new InvalidOperationException($"Qualification principal {index} requires unique, concrete direct permissions without wildcards.");
            permissions.Add(value);
        }
        return new(index, Required("subject"), Required("tenant"), Required("username"), Required("password"), permissions);
    }

    private static InvalidOperationException Failure(Entry entry, string reason) =>
        new($"Qualification principal {entry.Index}: {reason}.");

    private sealed record Entry(int Index, string Subject, string Tenant, string Username, string Password, IReadOnlySet<string> Permissions);
}
