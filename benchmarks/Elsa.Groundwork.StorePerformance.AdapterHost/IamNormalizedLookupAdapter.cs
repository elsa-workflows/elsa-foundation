using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The Groundwork v2 adapter for the frozen normalized Identity lookup/update workload. The workload
/// owns the correctness scenario and measured operation bodies; this leaf composes ASP.NET Core's public
/// UserManager/RoleManager contracts over Groundwork's stores and retains the live provider observation.
/// </summary>
internal sealed class IamNormalizedLookupAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IIamIdentityWorkloadAdapter
{
    internal const string PhysicalForm = "entity-type-specific-physical-tables-current-identity-shape";

    private RuntimeStoreComposition? composition;
    private RuntimeIdentityClient? client;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;
    // Matrix children share a configured provider, while the frozen fixture identities are intentionally
    // reused in every process. Keep the workload's logical tenant unchanged at this boundary, but derive a
    // process-specific physical scope so one child cannot collide with another child's correctness rows.
    private readonly string persistenceScope = PersistenceScopeFor(request);

    public IProviderRoundTripObserver? RoundTripObserver => composition?.Observer;

    internal string PersistenceScope => persistenceScope;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The iam-normalized-lookup-update operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (composition is not null)
            return;

        var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        var created = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            persistenceScope,
            cancellationToken,
            includeGroundworkIdentityStores: true);
        composition = created;
        client = created.CreateIdentityClient();
        observedProvider = observed;
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        RequirePrepared();
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var observed = observedProvider ?? throw new PerformanceContractException(
            "The iam-normalized-lookup-update adapter has no provider handshake; PrepareAsync must run first.");
        var workload = new IamNormalizedLookupWorkload();
        var result = await workload.ExecuteAsync(this, cancellationToken);
        operations = (await workload.PrepareMeasuredOperationsAsync(this, cancellationToken))
            .Select(operation => (IBenchmarkOperation)new BenchmarkOperation(operation))
            .ToArray();

        return new CorrectnessEvidence(
            result.ResultDigest,
            observed.Version,
            observed.Topology,
            observed.Configuration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                document.Routes));
    }

    public async Task<IdentityResult> CreateUserAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storageUser = ToStorageUser(user);
        var result = await RequireClient().Users.CreateAsync(storageUser);
        cancellationToken.ThrowIfCancellationRequested();
        CopyUserState(storageUser, user);
        return result;
    }

    public async Task<IdentityResult> CreateRoleAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await RequireClient().Roles.CreateAsync(role);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public async Task AddToRoleAsync(
        AspNetCoreIdentityUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storageUser = ToStorageUser(user);
        var result = await RequireClient().Users.AddToRoleAsync(storageUser, normalizedRoleName);
        cancellationToken.ThrowIfCancellationRequested();
        CopyUserState(storageUser, user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"link-user-role failed: {string.Join("; ", result.Errors.Select(error => error.Description))}");
    }

    public async Task<AspNetCoreIdentityUser?> FindUserByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireClient().Users.FindByNameAsync(normalizedName);
        cancellationToken.ThrowIfCancellationRequested();
        return user is null ? null : ToLogicalUser(user);
    }

    public async Task<AspNetCoreIdentityUser?> FindUserByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireClient().Users.FindByEmailAsync(normalizedEmail);
        cancellationToken.ThrowIfCancellationRequested();
        return user is null ? null : ToLogicalUser(user);
    }

    public async Task<IdentityRole?> FindRoleByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var role = await RequireClient().Roles.FindByNameAsync(normalizedName);
        cancellationToken.ThrowIfCancellationRequested();
        return role;
    }

    public async Task<IList<string>> GetRolesAsync(
        AspNetCoreIdentityUser user,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roles = await RequireClient().Users.GetRolesAsync(ToStorageUser(user));
        cancellationToken.ThrowIfCancellationRequested();
        return roles;
    }

    public async Task<IList<AspNetCoreIdentityUser>> GetUsersInRoleAsync(
        string normalizedRoleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = await RequireClient().Users.GetUsersInRoleAsync(normalizedRoleName);
        cancellationToken.ThrowIfCancellationRequested();
        return users.Select(ToLogicalUser).ToArray();
    }

    public async Task<AspNetCoreIdentityUser?> FindUserByIdAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await RequireClient().Users.FindByIdAsync(userId);
        cancellationToken.ThrowIfCancellationRequested();
        return user is null ? null : ToLogicalUser(user);
    }

    public async Task<IdentityResult> UpdateUserAsync(
        AspNetCoreIdentityUser user,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storageUser = ToStorageUser(user);
        var result = await RequireClient().Users.UpdateAsync(storageUser);
        cancellationToken.ThrowIfCancellationRequested();
        CopyUserState(storageUser, user);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.DisposeAsync();
        composition = null;
        client = null;
        observedProvider = null;
        operations = null;
    }

    private RuntimeStoreComposition RequirePrepared() =>
        composition ?? throw new PerformanceContractException(
            "The iam-normalized-lookup-update adapter has no composed backing; PrepareAsync must run first.");

    private RuntimeIdentityClient RequireClient() =>
        client ?? throw new PerformanceContractException(
            "The iam-normalized-lookup-update adapter has no Identity client; PrepareAsync must run first.");

    private AspNetCoreIdentityUser ToStorageUser(AspNetCoreIdentityUser user) => new()
    {
        Id = user.Id,
        TenantId = persistenceScope,
        UserName = user.UserName,
        NormalizedUserName = user.NormalizedUserName,
        Email = user.Email,
        NormalizedEmail = user.NormalizedEmail,
        EmailConfirmed = user.EmailConfirmed,
        PasswordHash = user.PasswordHash,
        SecurityStamp = user.SecurityStamp,
        ConcurrencyStamp = user.ConcurrencyStamp,
        PhoneNumber = user.PhoneNumber,
        PhoneNumberConfirmed = user.PhoneNumberConfirmed,
        TwoFactorEnabled = user.TwoFactorEnabled,
        LockoutEnd = user.LockoutEnd,
        LockoutEnabled = user.LockoutEnabled,
        AccessFailedCount = user.AccessFailedCount,
        DisplayName = user.DisplayName
    };

    private AspNetCoreIdentityUser ToLogicalUser(AspNetCoreIdentityUser user)
    {
        var logical = ToStorageUser(user);
        logical.TenantId = IamNormalizedLookupWorkload.TenantId;
        return logical;
    }

    private static void CopyUserState(AspNetCoreIdentityUser source, AspNetCoreIdentityUser target)
    {
        target.UserName = source.UserName;
        target.NormalizedUserName = source.NormalizedUserName;
        target.Email = source.Email;
        target.NormalizedEmail = source.NormalizedEmail;
        target.EmailConfirmed = source.EmailConfirmed;
        target.PasswordHash = source.PasswordHash;
        target.SecurityStamp = source.SecurityStamp;
        target.ConcurrencyStamp = source.ConcurrencyStamp;
        target.PhoneNumber = source.PhoneNumber;
        target.PhoneNumberConfirmed = source.PhoneNumberConfirmed;
        target.TwoFactorEnabled = source.TwoFactorEnabled;
        target.LockoutEnd = source.LockoutEnd;
        target.LockoutEnabled = source.LockoutEnabled;
        target.AccessFailedCount = source.AccessFailedCount;
        target.DisplayName = source.DisplayName;
    }

    private static string PersistenceScopeFor(RunRequest request)
    {
        var identity = string.Join(
            '|',
            request.ComparisonCohortId,
            request.MeasurementSetId,
            request.WorkloadId,
            request.WorkloadVersion,
            request.Provider,
            request.ProviderVersion,
            request.ProviderTopology,
            string.Join(';', request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")),
            request.Adapter,
            request.PhysicalForm,
            request.Scale,
            request.CommitSha,
            request.HarnessAssemblySha256,
            request.CompositionFingerprint,
            request.HostFingerprintSha256,
            request.Seed,
            request.InputFingerprintSha256,
            request.NativePlanIdentity,
            request.NativePlanEvidenceReference,
            request.NativePlanContentSha256,
            request.ProcessKind,
            request.ProcessIndex);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"benchmark-iam-{digest}";
    }

    private sealed class BenchmarkOperation(IIamNormalizedLookupWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
