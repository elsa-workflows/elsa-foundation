using System.Globalization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opaque snapshot bindings include account instance or role contents, so numeric revisions cannot alias a recreated root.</summary>
internal static class EfIamRevisionGuards
{
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
