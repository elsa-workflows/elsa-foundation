using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures the IAM native-route evidence from a fresh physical Identity catalog.
///
/// This is deliberately separate from the checkpoint capture path. The IAM contract needs a large,
/// real physical fixture and route-specific observations, whereas checkpoint-commit has no native
/// routes at all. The fixture is written through the public Identity row seam, and the route probes are
/// made through the same ASP.NET Core Identity managers that correctness and timing use.
/// </summary>
internal static class IamNativePlanCapture
{
    private const int PhysicalCardinality = 100_000;
    private const int BatchSize = 1_000;
    private const string RouteContract = "provider-native-routes";

    private static readonly IReadOnlyDictionary<string, RouteSpec> Routes =
        new Dictionary<string, RouteSpec>(StringComparer.Ordinal)
        {
            ["find-user-by-normalized-name"] = new(
                IdentityStorageManifest.IdentityUserDocumentKind,
                "identity_users",
                IdentityStorageManifest.NormalizedUserNameKeyField,
                IdentityV2StorageManifest.UserByNormalizedNameIndex),
            ["find-user-by-normalized-email"] = new(
                IdentityStorageManifest.IdentityUserDocumentKind,
                "identity_users",
                IdentityStorageManifest.NormalizedEmailKeyField,
                IdentityV2StorageManifest.UserByNormalizedEmailIndex),
            ["find-role-by-normalized-name"] = new(
                IdentityStorageManifest.IdentityRoleDocumentKind,
                "identity_roles",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                IdentityV2StorageManifest.RoleByNormalizedNameIndex),
            ["list-user-roles"] = new(
                IdentityStorageManifest.UserRoleDocumentKind,
                "identity_user_roles",
                IdentityStorageManifest.UserLookupKeyField,
                IdentityV2StorageManifest.UserRoleByUserIndex),
            ["list-role-users"] = new(
                IdentityStorageManifest.UserRoleDocumentKind,
                "identity_user_roles",
                IdentityStorageManifest.RoleLookupKeyField,
                IdentityV2StorageManifest.UserRoleByRoleIndex)
        };

    public static async Task<string> CaptureAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observed);
        if (!string.Equals(request.WorkloadId, IamNormalizedLookupWorkload.WorkloadId, StringComparison.Ordinal))
            throw new PerformanceContractException("IAM native-plan capture requires the iam-normalized-lookup-update workload.");
        if (!string.Equals(request.PhysicalForm, IamNormalizedLookupAdapter.PhysicalForm, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"IAM native-plan capture requires physical form '{IamNormalizedLookupAdapter.PhysicalForm}'.");
        if (!string.Equals(request.NativePlanEvidenceReference,
                           NativePlanEvidenceStaging.ReferenceFor(request.WorkloadId, request.Provider, request.MeasurementSetId),
                           StringComparison.Ordinal))
            throw new PerformanceContractException("IAM native-plan evidence must use the request-bound evidence reference.");
        EnsureObservedProvider(request, observed);

        // The provider is selected by the request and opened by the same Groundwork factory used by
        // correctness/timing. A capture therefore cannot silently substitute SQLite for another target.
        var observer = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        var persistenceScope = PersistenceScopeFor(request);
        await using var composition = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            persistenceScope,
            cancellationToken,
            observer,
            includeGroundworkIdentityStores: true);

        var rows = composition.CreateIdentityRowStore();
        await SeedAsync(rows, persistenceScope, cancellationToken);
        var counts = await VerifyPhysicalCardinalityAsync(rows, cancellationToken);

        // Ignore fixture setup and cardinality probes. Each raw plan is evidence for one manager route,
        // and only command events emitted while that route executes are retained. The Groundwork
        // provider sessions also have a deliberately opt-in native explain assertion seam. Its artifact
        // is captured beside each route and becomes the retained raw plan below; this keeps the manager
        // invocation as the behavioral source while proving the exact declared query's optimizer choice.
        observer.ClearCommands();
        var client = composition.CreateIdentityClient();
        var routeEvidence = new List<NativeRouteEvidence>(Routes.Count);
        var previousExplainFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
        var previousExplainDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
        var explainDirectory = Path.Combine(
            Path.GetTempPath(),
            $"groundwork-iam-explain-{request.Provider}-{request.MeasurementSetId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(explainDirectory);
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", explainDirectory);
        try
        {
            foreach (var route in IamNormalizedLookupWorkload.NativeRouteLimits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Routes.TryGetValue(route.Key, out var specification))
                    throw new PerformanceContractException($"IAM native route '{route.Key}' has no physical capture declaration.");

                observer.ClearCommands();
                var explainArtifactsBefore = Directory.EnumerateFiles(explainDirectory).ToHashSet(StringComparer.Ordinal);
                var materialized = await InvokeRouteAsync(route.Key, client, cancellationToken);
                var command = RequireRouteCommand(observer.Commands, specification, route.Key);
                var nativePlanPath = RequireNativePlanArtifact(
                    explainDirectory,
                    explainArtifactsBefore,
                    request.Provider,
                    specification,
                    route.Key);
                var nativePlan = IamNativePlanParser.Parse(
                    request.Provider,
                    File.ReadAllText(nativePlanPath));
                var normalizedPlan = IamNativePlanParser.NormalizeForArtifact(
                    request.Provider,
                    nativePlan.Content);
                // Parse the normalized bytes too: redaction must not turn a valid provider plan into
                // something that no longer proves the index-search fact retained in the evidence.
                nativePlan = IamNativePlanParser.Parse(request.Provider, normalizedPlan);

                var rawPlanReference = ArtifactStore.RawPlanName(
                    $"iam.{request.Provider}.{request.MeasurementSetId}.{route.Key}.raw{IamNativePlanParser.RawPlanExtension(request.Provider)}");
                var rawPlanPath = Path.Combine(outputDirectory, rawPlanReference);
                WriteRawNativePlan(rawPlanPath, normalizedPlan);

                routeEvidence.Add(new NativeRouteEvidence(
                    route.Key,
                    rawPlanReference,
                    NativePlanEvidenceStaging.Sha256(rawPlanPath),
                    nativePlan.PlanClassification,
                    nativePlan.PhysicalIndexName,
                    checked((int)counts[specification.UnitId]),
                    HasStorageScopePredicate(command),
                    HasRoutePredicate(command, specification.QueryField),
                    route.Value,
                    materialized));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousExplainFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousExplainDirectory);
            try
            {
                if (Directory.Exists(explainDirectory))
                    Directory.Delete(explainDirectory, recursive: true);
            }
            catch
            {
                // A retained raw route artifact is already complete; failure to clean this temporary
                // diagnostics directory must not mask the provider/capture result.
            }
        }

        var document = CreateDocument(request, observed, routeEvidence);
        return NativePlanEvidenceStaging.Write(outputDirectory, document);
    }

    private static NativePlanEvidenceDocument CreateDocument(
        RunRequest request,
        ProviderProbe.Result observed,
        IReadOnlyList<NativeRouteEvidence> routes) =>
        new(
            SchemaVersion: 2,
            ComparisonCohortId: request.ComparisonCohortId,
            MeasurementSetId: request.MeasurementSetId,
            WorkloadId: request.WorkloadId,
            WorkloadVersion: request.WorkloadVersion,
            Provider: request.Provider,
            Adapter: request.Adapter,
            PhysicalForm: request.PhysicalForm,
            Scale: request.Scale,
            CommitSha: request.CommitSha,
            HarnessAssemblySha256: request.HarnessAssemblySha256,
            CompositionFingerprint: request.CompositionFingerprint,
            HostFingerprintSha256: request.HostFingerprintSha256,
            ProviderVersion: observed.Version,
            ProviderTopology: observed.Topology,
            ProviderConfiguration: observed.Configuration,
            Seed: request.Seed,
            InputFingerprintSha256: request.InputFingerprintSha256,
            Identity: request.NativePlanIdentity,
            Routes: routes,
            RouteContract: RouteContract);

    private static async Task<Dictionary<string, long>> VerifyPhysicalCardinalityAsync(
        GroundworkIdentityRowStore rows,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var unitId in new[]
                 {
                     IdentityStorageManifest.IdentityUserDocumentKind,
                     IdentityStorageManifest.IdentityRoleDocumentKind,
                     IdentityStorageManifest.UserRoleDocumentKind
                 })
        {
            var result = rows.QueryWithTotalCount(
                unitId,
                new GroundworkIdentityRowQuery(
                    IdentityV2StorageManifest.IdField,
                    GroundworkIdentityRowComparison.GreaterThanOrEqual,
                    string.Empty,
                    IdentityV2StorageManifest.IdField,
                    Take: 1),
                cancellationToken);
            if (result.TotalCount != PhysicalCardinality)
                throw new PerformanceContractException(
                    $"IAM native-plan fixture unit '{unitId}' contains {result.TotalCount} physical records; expected exactly {PhysicalCardinality}.");
            counts.Add(unitId, result.TotalCount);
        }

        return counts;
    }

    private static async Task<int> InvokeRouteAsync(
        string route,
        RuntimeIdentityClient client,
        CancellationToken cancellationToken)
    {
        switch (route)
        {
            case "find-user-by-normalized-name":
                return await CountOneAsync(
                    await client.Users.FindByNameAsync(IamNormalizedLookupWorkload.NormalizedUserName),
                    "normalized-name",
                    cancellationToken);
            case "find-user-by-normalized-email":
                return await CountOneAsync(
                    await client.Users.FindByEmailAsync(IamNormalizedLookupWorkload.NormalizedEmail),
                    "normalized-email",
                    cancellationToken);
            case "find-role-by-normalized-name":
                return await CountOneAsync(
                    await client.Roles.FindByNameAsync(IamNormalizedLookupWorkload.NormalizedRoleName),
                    "role normalized-name",
                    cancellationToken);
            case "list-user-roles":
            {
                var user = await client.Users.FindByIdAsync(IamNormalizedLookupWorkload.UserId);
                if (user is null)
                    throw new PerformanceContractException("IAM native-plan fixture could not reload the canonical user.");
                var roles = await client.Users.GetRolesAsync(user);
                cancellationToken.ThrowIfCancellationRequested();
                if (roles.Count != 1 || !roles.Contains(IamNormalizedLookupWorkload.RoleName, StringComparer.Ordinal))
                    throw new PerformanceContractException("IAM list-user-roles route did not materialize exactly one canonical role.");
                return roles.Count;
            }
            case "list-role-users":
            {
                var users = await client.Users.GetUsersInRoleAsync(IamNormalizedLookupWorkload.NormalizedRoleName);
                cancellationToken.ThrowIfCancellationRequested();
                if (users.Count != 1 || users[0].Id != IamNormalizedLookupWorkload.UserId)
                    throw new PerformanceContractException("IAM list-role-users route did not materialize exactly one canonical user.");
                return users.Count;
            }
            default:
                throw new PerformanceContractException($"IAM native route '{route}' is not supported by the capture path.");
        }
    }

    private static Task<int> CountOneAsync<T>(T? value, string route, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value is null)
            throw new PerformanceContractException($"IAM {route} route did not materialize the canonical candidate.");
        return Task.FromResult(1);
    }

    private static ProviderCommandEvent RequireRouteCommand(
        IReadOnlyList<ProviderCommandEvent> commands,
        RouteSpec specification,
        string route)
    {
        var command = commands.LastOrDefault(command =>
            command.Operation.EndsWith(".query", StringComparison.Ordinal) &&
            command.CommandText is not null &&
            command.CommandText.Contains(specification.PhysicalName, StringComparison.Ordinal) &&
            command.CommandText.Contains(specification.QueryField, StringComparison.Ordinal));
        if (command.CommandText is null)
            throw new PerformanceContractException(
                $"IAM native route '{route}' did not emit an observable provider query against '{specification.PhysicalName}.{specification.QueryField}'.");
        return command;
    }

    private static bool HasStorageScopePredicate(ProviderCommandEvent command) =>
        command.CommandText?.Contains("__groundwork_scope", StringComparison.Ordinal) == true;

    private static bool HasRoutePredicate(ProviderCommandEvent command, string queryField) =>
        command.CommandText?.Contains(queryField, StringComparison.Ordinal) == true;

    private static string RequireNativePlanArtifact(
        string explainDirectory,
        IReadOnlySet<string> artifactsBefore,
        string provider,
        RouteSpec specification,
        string route)
    {
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var suffix = $"-{specification.IndexName}{extension}";
        var matches = Directory.EnumerateFiles(explainDirectory)
            .Where(path => !artifactsBefore.Contains(path))
            .Where(path => Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"IAM native route '{route}' must emit exactly one provider-native explain artifact for logical index '{specification.IndexName}'; observed {matches.Length}.");
        return matches[0];
    }

    private static void WriteRawNativePlan(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        ArtifactStore.ValidateRawPlanFile(path);
    }

    private static async Task SeedAsync(
        GroundworkIdentityRowStore rows,
        string persistenceScope,
        CancellationToken cancellationToken)
    {
        await WriteBatchesAsync(
            rows,
            Enumerable.Range(0, PhysicalCardinality).Select(index =>
                UserMutation(persistenceScope, index == 0 ? IamNormalizedLookupWorkload.UserId : $"plan-native-user-{index:D5}",
                    index == 0 ? IamNormalizedLookupWorkload.UserName : $"plan-native-user-{index:D5}",
                    index == 0 ? IamNormalizedLookupWorkload.NormalizedUserName : $"PLAN-NATIVE-USER-{index:D5}",
                    index == 0 ? IamNormalizedLookupWorkload.Email : $"plan-native-user-{index:D5}@example.test",
                    index == 0 ? IamNormalizedLookupWorkload.NormalizedEmail : $"PLAN-NATIVE-USER-{index:D5}@EXAMPLE.TEST")),
            cancellationToken);

        await WriteBatchesAsync(
            rows,
            Enumerable.Range(0, PhysicalCardinality).Select(index =>
                RoleMutation(persistenceScope, index == 0 ? IamNormalizedLookupWorkload.RoleId : $"plan-native-role-{index:D5}",
                    index == 0 ? IamNormalizedLookupWorkload.RoleName : $"Plan Native Role {index:D5}",
                    index == 0 ? IamNormalizedLookupWorkload.NormalizedRoleName : $"PLAN-NATIVE-ROLE-{index:D5}")),
            cancellationToken);

        await WriteBatchesAsync(
            rows,
            Enumerable.Range(0, PhysicalCardinality).Select(index =>
                LinkMutation(persistenceScope,
                    index == 0 ? IamNormalizedLookupWorkload.UserId : $"plan-native-link-user-{index:D5}",
                    index == 0 ? IamNormalizedLookupWorkload.RoleId : $"plan-native-link-role-{index:D5}")),
            cancellationToken);
    }

    private static async Task WriteBatchesAsync(
        GroundworkIdentityRowStore rows,
        IEnumerable<GroundworkIdentityRowMutation> mutations,
        CancellationToken cancellationToken)
    {
        var batch = new List<GroundworkIdentityRowMutation>(BatchSize);
        foreach (var mutation in mutations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batch.Add(mutation);
            if (batch.Count < BatchSize)
                continue;
            rows.WriteBatch(batch, cancellationToken);
            batch.Clear();
            await Task.Yield();
        }
        if (batch.Count > 0)
            rows.WriteBatch(batch, cancellationToken);
    }

    private static GroundworkIdentityRowMutation UserMutation(
        string scope,
        string id,
        string userName,
        string normalizedUserName,
        string email,
        string normalizedEmail)
    {
        var user = new UserRecord(
            id,
            scope,
            userName,
            email,
            userName,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
        var document = new IdentityUserDocument(
            scope,
            id,
            normalizedUserName,
            normalizedEmail,
            IdentityDocumentId.From(scope, normalizedUserName),
            IdentityDocumentId.From(scope, normalizedEmail),
            user);
        return GroundworkIdentityRowMutation.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(scope, id),
            JsonSerializer.Serialize(document, IdentityGroundworkJson.Options),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [IdentityStorageManifest.NormalizedUserNameKeyField] = document.NormalizedUserNameKey,
                [IdentityStorageManifest.NormalizedEmailKeyField] = document.NormalizedEmailKey
            },
            GroundworkIdentityRowWriteCondition.CreateOnly));
    }

    private static GroundworkIdentityRowMutation RoleMutation(string scope, string id, string name, string normalizedName)
    {
        var role = new RoleRecord(
            id,
            scope,
            name,
            null,
            new HashSet<string>(StringComparer.Ordinal),
            false);
        var document = new IdentityRoleDocument(
            scope,
            id,
            normalizedName,
            IdentityDocumentId.From(scope, normalizedName),
            role);
        return GroundworkIdentityRowMutation.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(scope, id),
            JsonSerializer.Serialize(document, IdentityGroundworkJson.Options),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [IdentityStorageManifest.NormalizedRoleNameKeyField] = document.NormalizedRoleNameKey,
                [IdentityStorageManifest.TenantIdField] = document.TenantId
            },
            GroundworkIdentityRowWriteCondition.CreateOnly));
    }

    private static GroundworkIdentityRowMutation LinkMutation(string scope, string userId, string roleId)
    {
        var document = new IdentityUserRoleDocument(
            scope,
            userId,
            roleId,
            IdentityDocumentId.From(scope, userId),
            IdentityDocumentId.From(scope, roleId));
        return GroundworkIdentityRowMutation.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.UserRoleDocumentKind,
            IdentityDocumentId.From(scope, userId, roleId),
            JsonSerializer.Serialize(document, IdentityGroundworkJson.Options),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [IdentityStorageManifest.UserLookupKeyField] = document.UserLookupKey,
                [IdentityStorageManifest.RoleLookupKeyField] = document.RoleLookupKey
            },
            GroundworkIdentityRowWriteCondition.CreateOnly));
    }

    private static void EnsureObservedProvider(RunRequest request, ProviderProbe.Result observed)
    {
        if (!string.Equals(observed.Provider, request.Provider, StringComparison.Ordinal) ||
            !string.Equals(observed.Version, request.ProviderVersion, StringComparison.Ordinal) ||
            !string.Equals(observed.Topology, request.ProviderTopology, StringComparison.Ordinal) ||
            !observed.Configuration.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new PerformanceContractException(
                "The live provider probe does not match the requested IAM native-plan provider identity, topology, or sanitized configuration.");
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
            request.NativePlanEvidenceReference);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        // IdentityV2StorageManifest caps document ids at 96 characters. Keep the physical scope
        // compact because composite user/role ids append a caller-owned identity to this value.
        return $"iam-native-{digest[..48]}";
    }

    private sealed record RouteSpec(string UnitId, string PhysicalName, string QueryField, string IndexName);
}
