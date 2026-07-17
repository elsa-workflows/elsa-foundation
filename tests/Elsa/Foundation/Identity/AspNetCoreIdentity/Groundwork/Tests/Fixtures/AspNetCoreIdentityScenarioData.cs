using System.Collections.Frozen;
using System.Globalization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

/// <summary>
/// The immutable input denominator shared by Identity unit, provider, restart, and workload scenarios.
/// Factories return fresh framework objects because ASP.NET Core Identity models are necessarily mutable.
/// </summary>
public static class AspNetCoreIdentityScenarioData
{
    private static readonly DateTimeOffset InitialInstant = new(2042, 2, 3, 4, 5, 6, TimeSpan.Zero);

    public static class Ids
    {
        public const string PrimaryTenant = "tenant-alpha";
        public const string SecondaryTenant = "tenant-beta";
        public const string PrimaryUser = "user-ada";
        public const string CrossTenantUser = "user-ada-beta";
        public const string UnicodeUser = "user-ipek";
        public const string AmbiguousEmailUserOne = "user-grace";
        public const string AmbiguousEmailUserTwo = "user-katherine";
        public const string PrimaryRole = "role-administrators";
        public const string CrossTenantRole = "role-administrators-beta";
        public const string UnicodeRole = "role-inceleyenler";
        public const string PrimaryUserClaim = "claim-user-read";
        public const string DuplicateUserClaim = "claim-user-read-duplicate";
        public const string PrimaryRoleClaim = "claim-role-manage";
        public const string PrimaryLogin = "login-entra-ada";
        public const string UnicodeLogin = "login-github-ipek";
        public const string PrimaryToken = "token-api-session";
        public const string AuthenticatorToken = "token-authenticator-key";
        public const string RecoveryToken = "token-recovery-codes";
        public const string PrimaryMembership = "membership-ada-alpha";
        public const string CrossTenantMembership = "membership-ada-beta";
    }

    public static ScenarioSecret AdministratorPassword { get; } =
        ScenarioSecret.Create("fixture-admin-password-v1");

    public static ScenarioSecret PrimaryPasswordHash { get; } =
        ScenarioSecret.Create("fixture-password-hash-ada-v1");

    public static ScenarioSecret PrimaryTokenValue { get; } =
        ScenarioSecret.Create("fixture-session-token-ada-v1");

    public static ScenarioSecret AuthenticatorKeyValue { get; } =
        ScenarioSecret.Create("fixture-authenticator-key-ada-v1");

    public static ScenarioSecret RecoveryCodeValue { get; } =
        ScenarioSecret.Create("fixture-recovery-codes-ada-v1");

    public static TimeProvider InitialClock { get; } = new FixedClock(InitialInstant);
    public static TimeProvider RestartClock { get; } = new FixedClock(InitialInstant.AddHours(1));
    public static TimeProvider AfterLockoutClock { get; } = new FixedClock(InitialInstant.AddMinutes(16));

    public static IReadOnlyList<TenantScope> TenantScopes { get; } = ReadOnly(
        new TenantScope(Ids.PrimaryTenant, ScopeKind.Ordinary, null),
        new TenantScope(Ids.SecondaryTenant, ScopeKind.Ordinary, null),
        new TenantScope(Ids.PrimaryTenant, ScopeKind.Privileged, "seed-initial-administrator"));

    public static IReadOnlyList<User> Users { get; } = ReadOnly(
        new User(
            Ids.PrimaryUser,
            Ids.PrimaryTenant,
            "ada",
            "ADA",
            "ada@example.test",
            "ADA@EXAMPLE.TEST",
            "Ada Lovelace",
            PrimaryPasswordHash,
            "security-ada-v1",
            "revision-ada-v1",
            true,
            "+31000000001",
            true,
            true,
            InitialInstant.AddMinutes(15),
            true,
            2,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            OrdinalSet(Ids.PrimaryRole),
            IgnoreCaseSet("identity.users.read")),
        new User(
            Ids.CrossTenantUser,
            Ids.SecondaryTenant,
            "ada",
            "ADA",
            "ada@example.test",
            "ADA@EXAMPLE.TEST",
            "Ada, Secondary Tenant",
            null,
            "security-ada-beta-v1",
            "revision-ada-beta-v1",
            false,
            null,
            false,
            false,
            null,
            true,
            0,
            UserStatus.Active,
            ResourceOwnership.External,
            OrdinalSet(Ids.CrossTenantRole),
            IgnoreCaseSet()),
        new User(
            Ids.UnicodeUser,
            Ids.PrimaryTenant,
            "İpek",
            "İPEK",
            "ipek@example.test",
            "IPEK@EXAMPLE.TEST",
            "İpek Yılmaz",
            null,
            "security-ipek-v1",
            "revision-ipek-v1",
            false,
            null,
            false,
            false,
            null,
            true,
            0,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            OrdinalSet(Ids.UnicodeRole),
            IgnoreCaseSet("identity.roles.read")),
        new User(
            Ids.AmbiguousEmailUserOne,
            Ids.PrimaryTenant,
            "grace",
            "GRACE",
            "shared@example.test",
            "SHARED@EXAMPLE.TEST",
            "Grace Hopper",
            null,
            "security-grace-v1",
            "revision-grace-v1",
            false,
            null,
            false,
            false,
            null,
            true,
            0,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            OrdinalSet(),
            IgnoreCaseSet()),
        new User(
            Ids.AmbiguousEmailUserTwo,
            Ids.PrimaryTenant,
            "katherine",
            "KATHERINE",
            "shared@example.test",
            "SHARED@EXAMPLE.TEST",
            "Katherine Johnson",
            null,
            "security-katherine-v1",
            "revision-katherine-v1",
            false,
            null,
            false,
            false,
            null,
            true,
            0,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            OrdinalSet(),
            IgnoreCaseSet()));

    public static IReadOnlyList<Role> Roles { get; } = ReadOnly(
        new Role(
            Ids.PrimaryRole,
            Ids.PrimaryTenant,
            "Administrators",
            "ADMINISTRATORS",
            "Full tenant administration",
            IgnoreCaseSet("*", "identity.users.manage", "identity.roles.manage"),
            true,
            "revision-role-admin-v1"),
        new Role(
            Ids.CrossTenantRole,
            Ids.SecondaryTenant,
            "Administrators",
            "ADMINISTRATORS",
            "Secondary tenant administration",
            IgnoreCaseSet("identity.users.read"),
            false,
            "revision-role-admin-beta-v1"),
        new Role(
            Ids.UnicodeRole,
            Ids.PrimaryTenant,
            "İnceleyenler",
            "İNCELEYENLER",
            "Unicode reviewer role",
            IgnoreCaseSet("identity.roles.read"),
            false,
            "revision-role-inceleyenler-v1"));

    public static IReadOnlyList<ClaimValue> Claims { get; } = ReadOnly(
        new ClaimValue(
            Ids.PrimaryUserClaim,
            Ids.PrimaryTenant,
            ClaimOwnerKind.User,
            Ids.PrimaryUser,
            "permission",
            "identity.users.read"),
        new ClaimValue(
            Ids.DuplicateUserClaim,
            Ids.PrimaryTenant,
            ClaimOwnerKind.User,
            Ids.PrimaryUser,
            "permission",
            "identity.users.read"),
        new ClaimValue(
            Ids.PrimaryRoleClaim,
            Ids.PrimaryTenant,
            ClaimOwnerKind.Role,
            Ids.PrimaryRole,
            "permission",
            "identity.users.manage"));

    public static IReadOnlyList<Login> Logins { get; } = ReadOnly(
        new Login(
            Ids.PrimaryLogin,
            Ids.PrimaryTenant,
            Ids.PrimaryUser,
            "entra",
            "subject-ada",
            "Microsoft Entra ID",
            InitialInstant,
            ExternalIdentityLinkPolicy.Admin),
        new Login(
            Ids.UnicodeLogin,
            Ids.PrimaryTenant,
            Ids.UnicodeUser,
            "github",
            "subject-İpek",
            "GitHub",
            InitialInstant.AddMinutes(1),
            ExternalIdentityLinkPolicy.Auto));

    public static IReadOnlyList<Token> Tokens { get; } = ReadOnly(
        new Token(
            Ids.PrimaryToken,
            Ids.PrimaryTenant,
            Ids.PrimaryUser,
            "elsa",
            "api-session",
            PrimaryTokenValue),
        new Token(
            Ids.AuthenticatorToken,
            Ids.PrimaryTenant,
            Ids.PrimaryUser,
            "[AspNetUserStore]",
            "AuthenticatorKey",
            AuthenticatorKeyValue),
        new Token(
            Ids.RecoveryToken,
            Ids.PrimaryTenant,
            Ids.PrimaryUser,
            "[AspNetUserStore]",
            "RecoveryCodes",
            RecoveryCodeValue));

    public static IReadOnlyList<Membership> Memberships { get; } = ReadOnly(
        new Membership(
            Ids.PrimaryMembership,
            Ids.PrimaryTenant,
            Ids.PrimaryUser,
            TenantMembershipStatus.Active,
            OrdinalSet(Ids.PrimaryRole),
            IgnoreCaseSet("identity.users.read")),
        new Membership(
            Ids.CrossTenantMembership,
            Ids.SecondaryTenant,
            Ids.CrossTenantUser,
            TenantMembershipStatus.Active,
            OrdinalSet(Ids.CrossTenantRole),
            IgnoreCaseSet()));

    public static IReadOnlyList<UnicodeCase> UnicodeCases { get; } = ReadOnly(
        new UnicodeCase("ascii-i", "i", "I", true),
        new UnicodeCase("turkish-dotted-i", "i", "İ", false),
        new UnicodeCase("turkish-dotless-i", "I", "ı", false),
        new UnicodeCase("sharp-s", "ß", "ẞ", true),
        new UnicodeCase("sharp-s-expansion", "ß", "ss", false),
        new UnicodeCase("greek-final-sigma", "Σ", "ς", true),
        new UnicodeCase("kelvin-sign", "K", "k", true),
        new UnicodeCase("ligature-expansion", "ﬀ", "FF", false),
        new UnicodeCase("precomposed-accent", "é", "É", true),
        new UnicodeCase("decomposed-accent", "é", "e\u0301", false),
        new UnicodeCase("garay-supplementary", "\U00010D70", "\U00010D50", true),
        new UnicodeCase("deseret-supplementary", "\U00010428", "\U00010400", true));

    public static string CanonicalInputFingerprint { get; } = CreateCanonicalInputFingerprint();

    public static IReadOnlyList<AspNetCoreIdentityScenarioObservation> ExpectedObservations { get; } = ReadOnly(
        AspNetCoreIdentityScenarioObservation.Text("input-fingerprint", CanonicalInputFingerprint),
        AspNetCoreIdentityScenarioObservation.Count("tenant-scope-count", TenantScopes.Count),
        AspNetCoreIdentityScenarioObservation.Count("user-count", Users.Count),
        AspNetCoreIdentityScenarioObservation.Count("role-count", Roles.Count),
        AspNetCoreIdentityScenarioObservation.Count("claim-count", Claims.Count),
        AspNetCoreIdentityScenarioObservation.Count("login-count", Logins.Count),
        AspNetCoreIdentityScenarioObservation.Count("token-count", Tokens.Count),
        AspNetCoreIdentityScenarioObservation.Count("membership-count", Memberships.Count),
        AspNetCoreIdentityScenarioObservation.Count("unicode-case-count", UnicodeCases.Count),
        AspNetCoreIdentityScenarioObservation.Text("primary-user-id", Ids.PrimaryUser),
        AspNetCoreIdentityScenarioObservation.Text("primary-role-id", Ids.PrimaryRole));

    public static AspNetCoreIdentityScenarioResult ExpectedResult { get; } =
        AspNetCoreIdentityScenarioResult.Create("identity-authority-baseline", ExpectedObservations);

    public static User PrimaryUser => Find(Users, Ids.PrimaryUser, user => user.Id);
    public static Role PrimaryRole => Find(Roles, Ids.PrimaryRole, role => role.Id);
    public static Membership PrimaryMembership => Find(Memberships, Ids.PrimaryMembership, membership => membership.Id);

    public static AspNetCoreIdentityUser CreateIdentityUser(User source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new()
        {
            Id = source.Id,
            TenantId = source.TenantId,
            UserName = source.UserName,
            NormalizedUserName = source.NormalizedUserName,
            Email = source.Email,
            NormalizedEmail = source.NormalizedEmail,
            EmailConfirmed = source.EmailConfirmed,
            PasswordHash = source.PasswordHash?.Reveal(),
            SecurityStamp = source.SecurityStamp,
            ConcurrencyStamp = source.ConcurrencyStamp,
            PhoneNumber = source.PhoneNumber,
            PhoneNumberConfirmed = source.PhoneNumberConfirmed,
            TwoFactorEnabled = source.TwoFactorEnabled,
            LockoutEnd = source.LockoutEnd,
            LockoutEnabled = source.LockoutEnabled,
            AccessFailedCount = source.AccessFailedCount,
            DisplayName = source.DisplayName
        };
    }

    public static IdentityRole CreateIdentityRole(Role source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new()
        {
            Id = source.Id,
            Name = source.Name,
            NormalizedName = source.NormalizedName,
            ConcurrencyStamp = source.ConcurrencyStamp
        };
    }

    public static System.Security.Claims.Claim CreateClaim(ClaimValue source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.Type, source.Value);
    }

    public static UserLoginInfo CreateLoginInfo(Login source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.LoginProvider, source.ProviderKey, source.ProviderDisplayName);
    }

    public static UserRecord CreateUserRecord(User source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.Id,
            source.TenantId,
            source.UserName,
            source.Email,
            source.DisplayName,
            source.Status,
            source.Ownership,
            source.RoleIds,
            source.DirectPermissions);
    }

    public static RoleRecord CreateRoleRecord(Role source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.Id,
            source.TenantId,
            source.Name,
            source.Description,
            source.Permissions,
            source.System);
    }

    public static ExternalIdentityRecord CreateExternalIdentityRecord(Login source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.TenantId,
            source.LoginProvider,
            source.ProviderKey,
            source.UserId,
            source.LinkedAt,
            source.LinkedAt,
            source.LinkPolicy);
    }

    public static TenantMembershipRecord CreateMembershipRecord(Membership source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(
            source.TenantId,
            source.UserId,
            source.Status,
            source.RoleIds,
            source.DirectPermissions);
    }

    private static string CreateCanonicalInputFingerprint()
    {
        var observations = ReadOnly(
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "tenant-scopes",
                TenantScopes.Select(scope => Canonical(
                    scope.TenantId,
                    scope.Kind.ToString(),
                    scope.Purpose))),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "users",
                Users.Select(user => Canonical(
                    user.Id,
                    user.TenantId,
                    user.UserName,
                    user.NormalizedUserName,
                    user.Email,
                    user.NormalizedEmail,
                    user.DisplayName,
                    user.PasswordHash?.Fingerprint,
                    user.SecurityStamp,
                    user.ConcurrencyStamp,
                    Boolean(user.EmailConfirmed),
                    user.PhoneNumber,
                    Boolean(user.PhoneNumberConfirmed),
                    Boolean(user.TwoFactorEnabled),
                    Timestamp(user.LockoutEnd),
                    Boolean(user.LockoutEnabled),
                    Number(user.AccessFailedCount),
                    user.Status.ToString(),
                    user.Ownership.ToString(),
                    CanonicalSet(user.RoleIds),
                    CanonicalSet(user.DirectPermissions)))),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "roles",
                Roles.Select(role => Canonical(
                    role.Id,
                    role.TenantId,
                    role.Name,
                    role.NormalizedName,
                    role.Description,
                    CanonicalSet(role.Permissions),
                    Boolean(role.System),
                    role.ConcurrencyStamp))),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "claims",
                Claims.Select(claim => Canonical(
                    claim.Id,
                    claim.TenantId,
                    claim.OwnerKind.ToString(),
                    claim.OwnerId,
                    claim.Type,
                    claim.Value))),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "logins",
                Logins.Select(login => Canonical(
                    login.Id,
                    login.TenantId,
                    login.UserId,
                    login.LoginProvider,
                    login.ProviderKey,
                    login.ProviderDisplayName,
                    Timestamp(login.LinkedAt),
                    login.LinkPolicy.ToString()))),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "tokens",
                Tokens.Select(token => Canonical(
                    token.Id,
                    token.TenantId,
                    token.UserId,
                    token.LoginProvider,
                    token.Name,
                    token.Value.Fingerprint))),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "memberships",
                Memberships.Select(membership => Canonical(
                    membership.Id,
                    membership.TenantId,
                    membership.UserId,
                    membership.Status.ToString(),
                    CanonicalSet(membership.RoleIds),
                    CanonicalSet(membership.DirectPermissions)))),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "clocks",
                new[]
                {
                    Timestamp(InitialClock.GetUtcNow()),
                    Timestamp(RestartClock.GetUtcNow()),
                    Timestamp(AfterLockoutClock.GetUtcNow())
                }),
            AspNetCoreIdentityScenarioObservation.OrderedValues(
                "unicode-cases",
                UnicodeCases.Select(value => Canonical(
                    value.Id,
                    value.Left,
                    value.Right,
                    Boolean(value.ExpectedEqualIgnoringCase)))));

        return AspNetCoreIdentityScenarioResult
            .Create("identity-scenario-input", observations)
            .CanonicalFingerprint;
    }

    private static T Find<T>(IEnumerable<T> values, string id, Func<T, string> idSelector) =>
        values.Single(value => string.Equals(idSelector(value), id, StringComparison.Ordinal));

    private static string Canonical(params string?[] values) => string.Concat(values.Select(Encode));

    private static string CanonicalSet(IEnumerable<string> values) =>
        string.Concat(values.OrderBy(value => value, StringComparer.Ordinal).Select(Encode));

    private static string Encode(string? value) => value is null ? "-1:" : $"{value.Length}:{value}";

    private static string Boolean(bool value) => value ? "true" : "false";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "-";

    private static IReadOnlySet<string> OrdinalSet(params string[] values) =>
        values.ToFrozenSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> IgnoreCaseSet(params string[] values) =>
        values.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<T> ReadOnly<T>(params T[] values) => Array.AsReadOnly(values);

    public enum ScopeKind
    {
        Ordinary,
        Privileged
    }

    public enum ClaimOwnerKind
    {
        User,
        Role
    }

    public sealed record TenantScope(string TenantId, ScopeKind Kind, string? Purpose);

    public sealed record User(
        string Id,
        string TenantId,
        string UserName,
        string NormalizedUserName,
        string? Email,
        string? NormalizedEmail,
        string? DisplayName,
        ScenarioSecret? PasswordHash,
        string SecurityStamp,
        string ConcurrencyStamp,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool PhoneNumberConfirmed,
        bool TwoFactorEnabled,
        DateTimeOffset? LockoutEnd,
        bool LockoutEnabled,
        int AccessFailedCount,
        UserStatus Status,
        ResourceOwnership Ownership,
        IReadOnlySet<string> RoleIds,
        IReadOnlySet<string> DirectPermissions);

    public sealed record Role(
        string Id,
        string TenantId,
        string Name,
        string NormalizedName,
        string? Description,
        IReadOnlySet<string> Permissions,
        bool System,
        string ConcurrencyStamp);

    public sealed record ClaimValue(
        string Id,
        string TenantId,
        ClaimOwnerKind OwnerKind,
        string OwnerId,
        string Type,
        string Value);

    public sealed record Login(
        string Id,
        string TenantId,
        string UserId,
        string LoginProvider,
        string ProviderKey,
        string ProviderDisplayName,
        DateTimeOffset LinkedAt,
        ExternalIdentityLinkPolicy LinkPolicy);

    public sealed record Token(
        string Id,
        string TenantId,
        string UserId,
        string LoginProvider,
        string Name,
        ScenarioSecret Value);

    public sealed record Membership(
        string Id,
        string TenantId,
        string UserId,
        TenantMembershipStatus Status,
        IReadOnlySet<string> RoleIds,
        IReadOnlySet<string> DirectPermissions);

    public sealed record UnicodeCase(
        string Id,
        string Left,
        string Right,
        bool ExpectedEqualIgnoringCase);

    public sealed class ScenarioSecret
    {
        private readonly string _value;

        private ScenarioSecret(string value)
        {
            _value = value;
            ScenarioResultCatalog.RegisterSensitiveValue(value);
            Fingerprint = AspNetCoreIdentityScenarioResult.FingerprintText(value);
        }

        public string Fingerprint { get; }

        public static ScenarioSecret Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\0', '\r', '\n']) >= 0)
                throw new ArgumentException("A nonblank, single-line fixture secret is required.", nameof(value));

            return new(value);
        }

        public string Reveal() => _value;

        public override string ToString() => "[redacted]";
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
