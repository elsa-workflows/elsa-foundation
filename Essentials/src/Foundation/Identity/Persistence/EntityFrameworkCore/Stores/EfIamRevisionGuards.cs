using System.Globalization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opaque snapshot bindings include account instance or role contents, so numeric revisions cannot alias a recreated root.</summary>
internal static class EfIamRevisionGuards
{
    public static bool OwnsConfiguredTarget(DbContext context)
    {
        if (!context.Database.IsRelational()) return false;
        var configured = context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single();
        return configured.Connection is null && !string.IsNullOrEmpty(configured.ConnectionString) &&
            string.Equals(configured.ConnectionString, context.Database.GetConnectionString(), StringComparison.Ordinal);
    }

    public static void Validate(IReadOnlyList<IamRevisionGuard> guards)
    {
        ArgumentNullException.ThrowIfNull(guards);
        if (guards.Count is < 1 or > 513 || guards.Any(guard => guard is null))
            throw new ArgumentException("Coordinated IAM guards must be present and bounded.", nameof(guards));
        if (guards.Select(guard => (guard.Kind, Tenant: EfIdentityStoreSupport.Normalize(guard.TenantId), Id: EfIdentityStoreSupport.Normalize(guard.Id)))
            .Distinct().Count() != guards.Count) throw new ArgumentException("Coordinated IAM guards must be unique.", nameof(guards));
    }

    public static async Task<bool> AdvanceAsync(IdentityIamDbContext context, IReadOnlyList<IamRevisionGuard> guards,
        CancellationToken cancellationToken, IamRevisionGuard? unchangedOwner = null)
    {
        Validate(guards);
        foreach (var guard in guards.OrderBy(item => item.Kind).ThenBy(item => item.TenantId, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!TryGetVersion(guard.Revision, out var version)) return false;
            var id = EfIdentityStoreSupport.RecordId(guard.TenantId, guard.Id);
            int changed;
            if (guard.Kind == IamRevisionGuardKind.User)
            {
                var row = await context.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.Revision == version, cancellationToken);
                if (row is null) return false;
                EfSchemaVersion.EnsureReadable(IdentityIamEfModule.Chain, row.SchemaVersion);
                if (User(row)?.Revision != guard.Revision) return false;
                // Qualified removal owns the counter advance for its target; keep its original guard intact.
                if (unchangedOwner is not null && guard.Kind == unchangedOwner.Kind && id == EfIdentityStoreSupport.RecordId(unchangedOwner.TenantId, unchangedOwner.Id)) continue;
                changed = await context.Users.Where(current => current.Id == id && current.Revision == version && current.SchemaVersion == row.SchemaVersion)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(current => current.Revision, checked(version + 1)), cancellationToken);
            }
            else
            {
                var row = await context.Roles.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.Revision == version, cancellationToken);
                if (row is null) return false;
                EfSchemaVersion.EnsureReadable(IdentityIamEfModule.Chain, row.SchemaVersion);
                if (Role(row).Revision != guard.Revision) return false;
                changed = await context.Roles.Where(current => current.Id == id && current.Revision == version && current.SchemaVersion == row.SchemaVersion)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(current => current.Revision, checked(version + 1)), cancellationToken);
            }
            if (changed != 1) return false;
        }
        return true;
    }

    public static IamRevisionGuard? User(UserEntity row) => string.IsNullOrWhiteSpace(row.SecurityStamp) ? null :
        new(IamRevisionGuardKind.User, row.TenantId, row.UserId, Token(row.Revision,
            IdentityEntityFrameworkKey.FramedRecordId("iam-user-guard/v1", row.Id, row.Revision.ToString(CultureInfo.InvariantCulture), row.SchemaVersion, row.SecurityStamp)));

    public static IamRevisionGuard Role(RoleEntity row) => new(IamRevisionGuardKind.Role, row.TenantId, row.RoleId, Token(row.Revision,
        IdentityEntityFrameworkKey.FramedRecordId("iam-role-guard/v1", row.Id, row.Revision.ToString(CultureInfo.InvariantCulture), row.SchemaVersion,
            row.TenantId, row.RoleId, row.Name, row.Description, row.PermissionsJson, row.System.ToString(), row.ClaimIdsJson, row.ConcurrencyStamp)));

    public static bool TryGetVersion(string value, out long version)
    {
        version = 0;
        return value is { Length: 89 } && value.StartsWith("ig1:", StringComparison.Ordinal) && value[24] == ':' &&
            value.Substring(25).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
            IdentityEntityFrameworkRevisionCodec.TryGetVersion("gw:" + value.Substring(4, 20), out version);
    }

    private static string Token(long version, string binding) => "ig1:" + version.ToString("D20", CultureInfo.InvariantCulture) + ":" + binding;
}
