using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>Provider-neutral, ratified observable IAM workload. Provider adapters call actual ASP.NET Core
/// Identity store contracts; Groundwork route evidence is intentionally represented separately.</summary>
public sealed class IamNormalizedLookupWorkload
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public const string WorkloadId = "iam-normalized-lookup-update";
    public const string ScenarioId = "identity-authority-baseline";
    public const string Seed = "spec095-iam-normalized-lookup-update-v1.1";
    public const string ExpectedInputFingerprint = "5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9";
    public const string ExpectedResultDigest = "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc";
    public const string TenantId = "tenant-alpha";
    public const string UserId = "user-ada";
    public const string RoleId = "role-admin";
    public const string UserName = "ada";
    public const string NormalizedUserName = "ADA";
    public const string Email = "ada@example.test";
    public const string NormalizedEmail = "ADA@EXAMPLE.TEST";
    public const string RoleName = "Administrators";
    public const string NormalizedRoleName = "ADMINISTRATORS";

    public static IReadOnlyList<string> OperationSequence { get; } =
    [
        "create-canonical-user",
        "create-noise-users",
        "create-canonical-role",
        "link-user-role",
        "find-user-by-normalized-name",
        "find-user-by-normalized-email",
        "find-role-by-normalized-name",
        "list-user-roles",
        "list-role-users",
        "accept-current-revision-update",
        "reject-stale-revision-update"
    ];

    public static IReadOnlyDictionary<string, int> NativeRouteLimits { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["find-user-by-normalized-name"] = 1,
            ["find-user-by-normalized-email"] = 2,
            ["find-role-by-normalized-name"] = 1,
            ["list-user-roles"] = 512,
            ["list-role-users"] = 512
        };

    public static IamNormalizedLookupInputDefinition InputDefinition { get; } = new(Seed, 1, 1, 16, 1, 1, 0, OperationSequence);

    public static string ComputeInputFingerprint() => Hash(JsonSerializer.Serialize(InputDefinition, CanonicalJsonOptions));

    public async ValueTask<IamNormalizedLookupResult> ExecuteAsync(IIamIdentityWorkloadAdapter adapter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (ComputeInputFingerprint() != ExpectedInputFingerprint)
            throw new InvalidOperationException("The Identity workload input definition no longer matches its ratified fingerprint.");

        var operations = new List<string>();
        var observableResults = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var user = CreateUser(UserId, UserName, NormalizedUserName, Email, NormalizedEmail);
        var role = CreateRole(RoleId, RoleName, NormalizedRoleName);

        AssertSucceeded(await adapter.CreateUserAsync(user, cancellationToken), "create-canonical-user");
        operations.Add("create-canonical-user");
        foreach (var noise in NoiseUsers())
            AssertSucceeded(await adapter.CreateUserAsync(noise, cancellationToken), "create-noise-user");

        operations.Add("create-noise-users");
        AssertSucceeded(await adapter.CreateRoleAsync(role, cancellationToken), "create-canonical-role");
        operations.Add("create-canonical-role");
        await adapter.AddToRoleAsync(user, role.NormalizedName!, cancellationToken);
        operations.Add("link-user-role");

        var byName = await adapter.FindUserByNormalizedNameAsync(NormalizedUserName, cancellationToken) ?? throw new InvalidOperationException("Identity workload could not find the canonical user by normalized name.");
        observableResults["find-user-by-normalized-name"] = byName.Id;
        operations.Add("find-user-by-normalized-name");
        var byEmail = await adapter.FindUserByNormalizedEmailAsync(NormalizedEmail, cancellationToken) ?? throw new InvalidOperationException("Identity workload could not find the canonical user by normalized email.");
        observableResults["find-user-by-normalized-email"] = byEmail.Id;
        operations.Add("find-user-by-normalized-email");
        var roleByName = await adapter.FindRoleByNormalizedNameAsync(NormalizedRoleName, cancellationToken) ?? throw new InvalidOperationException("Identity workload could not find the canonical role by normalized name.");
        observableResults["find-role-by-normalized-name"] = roleByName.Id;
        operations.Add("find-role-by-normalized-name");
        observableResults["list-user-roles"] = string.Join(',', (await adapter.GetRolesAsync(byName, cancellationToken)).Order(StringComparer.Ordinal));
        operations.Add("list-user-roles");
        observableResults["list-role-users"] = string.Join(',', (await adapter.GetUsersInRoleAsync(NormalizedRoleName, cancellationToken)).Select(candidate => candidate.Id).Order(StringComparer.Ordinal));
        operations.Add("list-role-users");

        var stale = byName;
        var current = await adapter.FindUserByIdAsync(user.Id, cancellationToken) ?? throw new InvalidOperationException("Identity workload could not reload the canonical user by id.");
        current.DisplayName = "Ada Updated";
        AssertSucceeded(await adapter.UpdateUserAsync(current, cancellationToken), "accept-current-revision-update");
        observableResults["current-update-display-name"] = current.DisplayName ?? string.Empty;
        operations.Add("accept-current-revision-update");
        stale.DisplayName = "Ada Stale";
        if ((await adapter.UpdateUserAsync(stale, cancellationToken)).Succeeded)
            throw new InvalidOperationException("Identity workload accepted a stale revision update.");
        observableResults["stale-revision-update"] = "rejected";
        operations.Add("reject-stale-revision-update");

        var digest = ComputeResultDigest(operations, observableResults);
        if (digest != ExpectedResultDigest)
            throw new InvalidOperationException("The Identity workload observations no longer match the ratified result digest.");

        return new IamNormalizedLookupResult(ComputeInputFingerprint(), digest, operations, observableResults);
    }

    public static IEnumerable<AspNetCoreIdentityUser> NoiseUsers()
    {
        for (var index = 0; index < 16; index++)
        {
            var suffix = index.ToString("D4");
            yield return CreateUser(
                $"user-plan-noise-{suffix}",
                $"plan-noise-{suffix}",
                $"PLAN-NOISE-{suffix}",
                $"plan-noise-{suffix}@example.test",
                $"PLAN-NOISE-{suffix}@EXAMPLE.TEST");
        }
    }

    public static AspNetCoreIdentityUser CreateUser(
        string id,
        string userName,
        string normalizedUserName,
        string email,
        string normalizedEmail) => new()
        {
            Id = id,
            TenantId = TenantId,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = "Ada Lovelace",
            EmailConfirmed = true,
            PhoneNumber = "+31000000001",
            PhoneNumberConfirmed = true,
            LockoutEnabled = true,
            TwoFactorEnabled = true,
            SecurityStamp = "security-ada-v1",
            ConcurrencyStamp = "revision-ada-v1"
        };

    public static IdentityRole CreateRole(string id, string name, string normalizedName) => new()
    {
        Id = id,
        Name = name,
        NormalizedName = normalizedName,
        ConcurrencyStamp = "revision-role-admin-v1"
    };

    public static string ComputeResultDigest(
        IReadOnlyList<string> operations,
        IReadOnlyDictionary<string, string> observableResults) =>
        Hash(JsonSerializer.Serialize(new
        {
            WorkloadId,
            ScenarioId,
            InputFingerprint = ExpectedInputFingerprint,
            Operations = operations,
            ObservableResults = observableResults
        }));

    public static void AssertSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{operation} failed: {string.Join("; ", result.Errors.Select(error => error.Description))}");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public interface IIamIdentityWorkloadAdapter
{
    Task<IdentityResult> CreateUserAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken);
    Task<IdentityResult> CreateRoleAsync(IdentityRole role, CancellationToken cancellationToken);
    Task AddToRoleAsync(AspNetCoreIdentityUser user, string normalizedRoleName, CancellationToken cancellationToken);
    Task<AspNetCoreIdentityUser?> FindUserByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken);
    Task<AspNetCoreIdentityUser?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<IdentityRole?> FindRoleByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken);
    Task<IList<string>> GetRolesAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken);
    Task<IList<AspNetCoreIdentityUser>> GetUsersInRoleAsync(string normalizedRoleName, CancellationToken cancellationToken);
    Task<AspNetCoreIdentityUser?> FindUserByIdAsync(string userId, CancellationToken cancellationToken);
    Task<IdentityResult> UpdateUserAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken);
}

public sealed record IamNormalizedLookupResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, string> ObservableResults);

public sealed record IamNormalizedLookupInputDefinition(
    string Seed,
    int TenantCount,
    int CanonicalUserCount,
    int NoiseUserCount,
    int RoleCount,
    int UserRoleLinkCount,
    int ConcurrentContenders,
    IReadOnlyList<string> OperationSequence);
