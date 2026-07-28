using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
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
        var evidence = ValidateNativeRouteEvidence(provider.ProviderKey, target.NativeRouteEvidence);
        var client = target.Client;
        var boundedStore = client.BoundedDocumentStore
                           ?? throw new InvalidOperationException("The Identity correctness workload requires a physical bounded-query target.");
        var access = new AspNetCoreIdentityObservableScenario.FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(IamNormalizedLookupWorkload.TenantId)));
        var users = new GroundworkIdentityUserStore(client.DocumentStore, access, boundedStore);
        var roles = new GroundworkIdentityRoleStore(client.DocumentStore, access, boundedStore);
        var result = await new IamNormalizedLookupWorkload().ExecuteAsync(new GroundworkIdentityWorkloadAdapter(users, roles), cancellationToken);

        return new AspNetCoreIdentityWorkloadResult(
            provider.ProviderIdentity,
            WorkloadId,
            ScenarioId,
            result.InputFingerprint,
            result.ResultDigest,
            result.ObservableOperations,
            result.ObservableResults,
            evidence);
    }

    internal static IReadOnlyList<ProviderNativePlanEvidence> ValidateNativeRouteEvidence(
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
            var expectedLimit = IamNormalizedLookupWorkload.NativeRouteLimits[route];
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
            return new ProviderNativePlanEvidence(
                route,
                result.Evidence.Sha256,
                result.PlanClassification,
                result.IndexName,
                result.FiniteLimit!.Value);
        }).ToArray();
    }

}

internal sealed class GroundworkIdentityWorkloadAdapter(GroundworkIdentityUserStore users, GroundworkIdentityRoleStore roles)
    : IIamIdentityWorkloadAdapter
{
    public Task<IdentityResult> CreateUserAsync(AspNetCoreIdentityUser user, CancellationToken token) => users.CreateAsync(user, token);
    public Task<IdentityResult> CreateRoleAsync(IdentityRole role, CancellationToken token) => roles.CreateAsync(role, token);
    public Task AddToRoleAsync(AspNetCoreIdentityUser user, string role, CancellationToken token) => users.AddToRoleAsync(user, role, token);
    public Task<AspNetCoreIdentityUser?> FindUserByNormalizedNameAsync(string value, CancellationToken token) => users.FindByNameAsync(value, token);
    public Task<AspNetCoreIdentityUser?> FindUserByNormalizedEmailAsync(string value, CancellationToken token) => users.FindByEmailAsync(value, token);
    public Task<IdentityRole?> FindRoleByNormalizedNameAsync(string value, CancellationToken token) => roles.FindByNameAsync(value, token);
    public Task<IList<string>> GetRolesAsync(AspNetCoreIdentityUser user, CancellationToken token) => users.GetRolesAsync(user, token);
    public Task<IList<AspNetCoreIdentityUser>> GetUsersInRoleAsync(string value, CancellationToken token) => users.GetUsersInRoleAsync(value, token);
    public Task<AspNetCoreIdentityUser?> FindUserByIdAsync(string value, CancellationToken token) => users.FindByIdAsync(value, token);
    public Task<IdentityResult> UpdateUserAsync(AspNetCoreIdentityUser user, CancellationToken token) => users.UpdateAsync(user, token);
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
    IReadOnlyList<ProviderNativePlanEvidence> NativeRouteEvidence);

internal static class AspNetCoreIdentityObservableScenario
{
    public const string TenantId = IamNormalizedLookupWorkload.TenantId;
    public const string UserId = IamNormalizedLookupWorkload.UserId;
    public const string RoleId = IamNormalizedLookupWorkload.RoleId;
    public const string UserName = IamNormalizedLookupWorkload.UserName;
    public const string NormalizedUserName = IamNormalizedLookupWorkload.NormalizedUserName;
    public const string Email = IamNormalizedLookupWorkload.Email;
    public const string NormalizedEmail = IamNormalizedLookupWorkload.NormalizedEmail;
    public const string RoleName = IamNormalizedLookupWorkload.RoleName;
    public const string NormalizedRoleName = IamNormalizedLookupWorkload.NormalizedRoleName;
    public const string WorkloadId = IamNormalizedLookupWorkload.WorkloadId;
    public const string ScenarioId = IamNormalizedLookupWorkload.ScenarioId;
    public const string ExpectedResultDigest = IamNormalizedLookupWorkload.ExpectedResultDigest;
    public const string ExpectedInputFingerprint = IamNormalizedLookupWorkload.ExpectedInputFingerprint;

    public static IReadOnlyList<string> Operations => IamNormalizedLookupWorkload.OperationSequence;
    public static IReadOnlyDictionary<string, int> NativeRouteLimits => IamNormalizedLookupWorkload.NativeRouteLimits;
    public static IamNormalizedLookupInputDefinition InputDefinition => IamNormalizedLookupWorkload.InputDefinition;

    public static IEnumerable<AspNetCoreIdentityUser> NoiseUsers() => IamNormalizedLookupWorkload.NoiseUsers();

    public static AspNetCoreIdentityUser CreateUser(
        string id,
        string userName,
        string normalizedUserName,
        string email,
        string normalizedEmail) => IamNormalizedLookupWorkload.CreateUser(id, userName, normalizedUserName, email, normalizedEmail);

    public static IdentityRole CreateRole(string id, string name, string normalizedName) => IamNormalizedLookupWorkload.CreateRole(id, name, normalizedName);

    public static string ComputeInputFingerprint() => IamNormalizedLookupWorkload.ComputeInputFingerprint();

    public static string ComputeResultDigest(
        IReadOnlyList<string> operations,
        IReadOnlyDictionary<string, string> observableResults)
        => IamNormalizedLookupWorkload.ComputeResultDigest(operations, observableResults);

    public static void AssertSucceeded(IdentityResult result, string operation)
        => IamNormalizedLookupWorkload.AssertSucceeded(result, operation);

    public sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
