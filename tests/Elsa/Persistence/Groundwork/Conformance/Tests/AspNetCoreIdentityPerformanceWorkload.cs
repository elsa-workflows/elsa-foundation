using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityPerformanceWorkload
{
    public const int NativeEvidenceAcceptanceCardinality = 100_000;
    public const string WorkloadId = AspNetCoreIdentityObservableScenario.WorkloadId;
    public const string ScenarioId = AspNetCoreIdentityObservableScenario.ScenarioId;
    public const string ExpectedResultDigest = AspNetCoreIdentityObservableScenario.ExpectedResultDigest;
    public const string ExpectedInputFingerprint = AspNetCoreIdentityObservableScenario.ExpectedInputFingerprint;

    public static string[] RequiredProviders { get; } =
        ["sqlite", "sqlserver", "postgresql", "mongodb"];

    public static string[] RequiredNativeRoutes { get; } =
    [
        "find-user-by-normalized-name",
        "find-user-by-normalized-email",
        "find-role-by-normalized-name",
        "list-user-roles",
        "list-role-users"
    ];

    public static string ComputeInputFingerprint() => AspNetCoreIdentityObservableScenario.ComputeInputFingerprint();

    public async ValueTask<AspNetCoreIdentityWorkloadResult> ExecuteAsync(
        AspNetCoreIdentityPhysicalWorkloadTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.Client);
        var provider = target.Client.OwnerDescriptor;
        var inputFingerprint = ComputeInputFingerprint();
        if (!string.Equals(inputFingerprint, ExpectedInputFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The Identity workload input definition no longer matches its ratified fingerprint.");

        var evidence = ValidateNativeRouteEvidence(provider.ProviderKey, target.NativeRouteEvidence);
        var client = target.Client;
        var boundedStore = client.BoundedDocumentStore
                           ?? throw new InvalidOperationException("The Identity correctness workload requires a physical bounded-query target.");
        var access = new AspNetCoreIdentityObservableScenario.FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(AspNetCoreIdentityObservableScenario.TenantId)));
        var users = new GroundworkIdentityUserStore(client.DocumentStore, access, boundedStore);
        var roles = new GroundworkIdentityRoleStore(client.DocumentStore, access, boundedStore);
        var operations = new List<string>();
        var observableResults = new SortedDictionary<string, string>(StringComparer.Ordinal);

        var user = AspNetCoreIdentityObservableScenario.CreateUser(
            AspNetCoreIdentityObservableScenario.UserId,
            AspNetCoreIdentityObservableScenario.UserName,
            AspNetCoreIdentityObservableScenario.NormalizedUserName,
            AspNetCoreIdentityObservableScenario.Email,
            AspNetCoreIdentityObservableScenario.NormalizedEmail);
        var role = AspNetCoreIdentityObservableScenario.CreateRole(
            AspNetCoreIdentityObservableScenario.RoleId,
            AspNetCoreIdentityObservableScenario.RoleName,
            AspNetCoreIdentityObservableScenario.NormalizedRoleName);

        AspNetCoreIdentityObservableScenario.AssertSucceeded(await users.CreateAsync(user, cancellationToken), "create-canonical-user");
        operations.Add("create-canonical-user");

        foreach (var noise in AspNetCoreIdentityObservableScenario.NoiseUsers())
        {
            AspNetCoreIdentityObservableScenario.AssertSucceeded(await users.CreateAsync(noise, cancellationToken), "create-noise-user");
        }

        operations.Add("create-noise-users");
        AspNetCoreIdentityObservableScenario.AssertSucceeded(await roles.CreateAsync(role, cancellationToken), "create-canonical-role");
        operations.Add("create-canonical-role");
        await users.AddToRoleAsync(user, role.NormalizedName!, cancellationToken);
        operations.Add("link-user-role");

        var byName = await users.FindByNameAsync(AspNetCoreIdentityObservableScenario.NormalizedUserName, cancellationToken)
                     ?? throw new InvalidOperationException("Groundwork workload failed to find the canonical user by normalized name.");
        observableResults["find-user-by-normalized-name"] = byName.Id;
        operations.Add("find-user-by-normalized-name");

        var byEmail = await users.FindByEmailAsync(AspNetCoreIdentityObservableScenario.NormalizedEmail, cancellationToken)
                      ?? throw new InvalidOperationException("Groundwork workload failed to find the canonical user by normalized email.");
        observableResults["find-user-by-normalized-email"] = byEmail.Id;
        operations.Add("find-user-by-normalized-email");

        var roleByName = await roles.FindByNameAsync(AspNetCoreIdentityObservableScenario.NormalizedRoleName, cancellationToken)
                         ?? throw new InvalidOperationException("Groundwork workload failed to find the canonical role by normalized name.");
        observableResults["find-role-by-normalized-name"] = roleByName.Id;
        operations.Add("find-role-by-normalized-name");

        var userRoles = await users.GetRolesAsync(byName, cancellationToken);
        observableResults["list-user-roles"] = string.Join(',', userRoles.Order(StringComparer.Ordinal));
        operations.Add("list-user-roles");

        var usersInRole = await users.GetUsersInRoleAsync(AspNetCoreIdentityObservableScenario.NormalizedRoleName, cancellationToken);
        observableResults["list-role-users"] = string.Join(',', usersInRole.Select(candidate => candidate.Id).Order(StringComparer.Ordinal));
        operations.Add("list-role-users");

        var stale = byName;
        var current = await users.FindByIdAsync(user.Id, cancellationToken)
                      ?? throw new InvalidOperationException("Groundwork workload failed to reload the canonical user by id.");
        current.DisplayName = "Ada Updated";
        AspNetCoreIdentityObservableScenario.AssertSucceeded(await users.UpdateAsync(current, cancellationToken), "accept-current-revision-update");
        observableResults["current-update-display-name"] = current.DisplayName ?? string.Empty;
        operations.Add("accept-current-revision-update");

        stale.DisplayName = "Ada Stale";
        var staleResult = await users.UpdateAsync(stale, cancellationToken);
        if (staleResult.Succeeded)
            throw new InvalidOperationException("Groundwork workload accepted a stale revision update.");
        observableResults["stale-revision-update"] = "rejected";
        operations.Add("reject-stale-revision-update");

        var digest = AspNetCoreIdentityObservableScenario.ComputeResultDigest(operations, observableResults);
        if (!string.Equals(digest, ExpectedResultDigest, StringComparison.Ordinal))
            throw new InvalidOperationException("The Identity workload observations no longer match the ratified result digest.");

        return new AspNetCoreIdentityWorkloadResult(
            provider.ProviderIdentity,
            WorkloadId,
            ScenarioId,
            inputFingerprint,
            digest,
            operations,
            observableResults,
            evidence);
    }

    internal static IReadOnlyList<AspNetCoreIdentityNativeRouteEvidenceReference> ValidateNativeRouteEvidence(
        string providerKey,
        IReadOnlyCollection<GroundworkNativeRoutePlanResult> nativeRouteEvidence)
    {
        ArgumentNullException.ThrowIfNull(nativeRouteEvidence);
        if (nativeRouteEvidence.Count != RequiredNativeRoutes.Length)
            throw new InvalidOperationException(
                $"The physical Identity workload requires exactly {RequiredNativeRoutes.Length} native route evidence records.");

        var byRoute = nativeRouteEvidence.ToDictionary(result => result.Request.QueryIdentity, StringComparer.Ordinal);
        var missing = RequiredNativeRoutes.Except(byRoute.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"The physical Identity workload is missing native route evidence: {string.Join(", ", missing)}.");

        return RequiredNativeRoutes.Select(route =>
        {
            var result = byRoute[route];
            var expectedLimit = AspNetCoreIdentityObservableScenario.NativeRouteLimits[route];
            if (!string.Equals(result.ProviderKey, providerKey, StringComparison.Ordinal) ||
                result.PhysicalCardinality != NativeEvidenceAcceptanceCardinality ||
                !result.HasStorageScopePredicate ||
                !result.HasRoutePredicate ||
                result.FiniteLimit != expectedLimit ||
                result.Request.Limit != expectedLimit ||
                result.MaterializedCandidateCount != 1 ||
                string.IsNullOrWhiteSpace(result.PlanClassification) ||
                string.IsNullOrWhiteSpace(result.IndexName) ||
                !string.Equals(result.Evidence.Kind, "identity-native-route-plan", StringComparison.Ordinal))
                throw new InvalidOperationException($"Native route evidence for '{route}' does not satisfy the admitted evidence contract.");
            return new AspNetCoreIdentityNativeRouteEvidenceReference(
                route,
                result.Evidence.Sha256,
                result.PlanClassification,
                result.IndexName,
                result.FiniteLimit);
        }).ToArray();
    }
}

public sealed record AspNetCoreIdentityPhysicalWorkloadTarget(
    GroundworkProviderClient Client,
    IReadOnlyCollection<GroundworkNativeRoutePlanResult> NativeRouteEvidence);

public sealed class AspNetCoreIdentityEfContractBaseline
{
    public const string BaselineIdentity = "frozen-ef-contract-snapshot";
    public const string ExecutionStatus = "not-executed";
    public const string ExecutionOwner = "#646";
    public const string WorkloadId = AspNetCoreIdentityObservableScenario.WorkloadId;
    public const string ExpectedInputFingerprint = AspNetCoreIdentityObservableScenario.ExpectedInputFingerprint;
    public const string ExpectedResultDigest = AspNetCoreIdentityObservableScenario.ExpectedResultDigest;
}

public sealed record AspNetCoreIdentityWorkloadResult(
    string ProviderIdentity,
    string WorkloadId,
    string ScenarioId,
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, string> ObservableResults,
    IReadOnlyList<AspNetCoreIdentityNativeRouteEvidenceReference> NativeRouteEvidence);

public sealed record AspNetCoreIdentityNativeRouteEvidenceReference(
    string RouteIdentity,
    string EvidenceDigest,
    string PlanClassification,
    string IndexName,
    int FiniteLimit);

internal static class AspNetCoreIdentityObservableScenario
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public const string TenantId = "tenant-alpha";
    public const string UserId = "user-ada";
    public const string RoleId = "role-admin";
    public const string UserName = "ada";
    public const string NormalizedUserName = "ADA";
    public const string Email = "ada@example.test";
    public const string NormalizedEmail = "ADA@EXAMPLE.TEST";
    public const string RoleName = "Administrators";
    public const string NormalizedRoleName = "ADMINISTRATORS";
    public const string WorkloadId = "iam-normalized-lookup-update";
    public const string ScenarioId = "identity-authority-baseline";
    public const string ExpectedResultDigest = "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc";
    public const string ExpectedInputFingerprint = "5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9";

    public static IReadOnlyList<string> Operations { get; } =
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

    public static AspNetCoreIdentityWorkloadInputDefinition InputDefinition { get; } = new(
        "spec095-iam-normalized-lookup-update-v1.1",
        1,
        1,
        16,
        1,
        1,
        0,
        Operations);

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

    public static string ComputeInputFingerprint()
    {
        var payload = JsonSerializer.Serialize(InputDefinition, CanonicalJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static string ComputeResultDigest(
        IReadOnlyList<string> operations,
        IReadOnlyDictionary<string, string> observableResults)
    {
        var payload = JsonSerializer.Serialize(new
        {
            WorkloadId,
            ScenarioId,
            InputFingerprint = ExpectedInputFingerprint,
            Operations = operations,
            ObservableResults = observableResults
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static void AssertSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{operation} failed: {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }

    public sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}

public sealed record AspNetCoreIdentityWorkloadInputDefinition(
    string Seed,
    int TenantCount,
    int CanonicalUserCount,
    int NoiseUserCount,
    int RoleCount,
    int UserRoleLinkCount,
    int ConcurrentContenders,
    IReadOnlyList<string> OperationSequence);
