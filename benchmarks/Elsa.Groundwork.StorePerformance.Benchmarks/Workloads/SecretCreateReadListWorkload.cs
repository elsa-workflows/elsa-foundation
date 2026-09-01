using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// The provider-neutral correctness contract for the frozen Secret workload.  This type only knows
/// the public Secret repository contract; provider-specific EF entities and connections belong to the
/// adapter host.
/// </summary>
public sealed class SecretCreateReadListWorkload
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly DateTimeOffset FixtureNow = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    public const string WorkloadId = "secret-create-read-list";
    public const string ScenarioId = "secret-create-read-list-baseline";
    public const string Version = "1.1.0";
    public const string Seed = "spec094-secret-create-read-list-v1.1";
    public const string HistoricalSeed = "spec094-secret-create-read-list-v1";
    public const string HistoricalInputFingerprint = "339a6adc9ba6c34e85ce43eafd3e0b8b7b74f7ccbb7d52bd34efe1fbe394014c";
    public const string HistoricalResultDigest = "615f7bbd8e160dd34d38180d5def0e99d0b4225822e6ebee5ea31ed21bbabcdb";
    public const string ExpectedInputFingerprint = "7f64dd6942e976e2cea5ad84db1704f4b6239380136a93d99a6480f5909021ce";
    // The historical v1.0 digest is retained in ReproducibleWorkloadScenarioCatalog. It predates an
    // executable serializer, so this leaf freezes the recomputed digest of the explicit vector below.
    public const string ExpectedResultDigest = "394ff58bd146744fe30f4abd3a8529ab1287129787d40e188ffc0c58038e8783";
    public const int TenantCount = 2;
    public const int CanonicalSecretCount = 3;
    public const int NoiseSecretCount = 64;
    public const int PageSize = 16;
    public const int ConcurrentContenders = 2;
    public const string PrimaryTenantId = "tenant-alpha";
    public const string SecondaryTenantId = "tenant-beta";
    public const string WinnerSecretId = "secret-contender-winner";
    public const string WinnerSecretName = "shared-secret";
    public const string WinnerSecretValue = "secret-winner-value";

    public static IReadOnlyList<string> OperationSequence { get; } =
    [
        "create-canonical-secrets",
        "create-noise-secrets",
        "concurrent-create-same-secret",
        "read-create-winner-by-identity",
        "list-secrets-bounded-first-page",
        "list-secrets-bounded-next-offset-page"
    ];

    public static string ComputeInputFingerprint() =>
        Hash(JsonSerializer.Serialize(new
        {
            WorkloadId,
            ScenarioId,
            Seed,
            Parameters = new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["canonicalSecretCount"] = CanonicalSecretCount,
                ["concurrentContenders"] = ConcurrentContenders,
                ["noiseSecretCount"] = NoiseSecretCount,
                ["pageSize"] = PageSize,
                ["tenantCount"] = TenantCount,
                ["timedSetup"] = "excluded"
            },
            OperationSequence
        }, CanonicalJsonOptions));

    public async ValueTask<SecretCreateReadListResult> ExecuteAsync(
        ISecretCreateReadListWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ValidateFrozenContract();

        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        var primary = scopes.Primary;
        var secondary = scopes.Secondary;
        var operations = new List<string>();
        var observableResults = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var secret in CanonicalSecrets(PrimaryTenantId))
            Assert(await primary.TryAddAsync(secret, cancellationToken), "create-canonical-secrets");
        operations.Add(OperationSequence[0]);

        foreach (var secret in NoiseSecrets(PrimaryTenantId))
            Assert(await primary.TryAddAsync(secret, cancellationToken), "create-noise-secrets");
        operations.Add(OperationSequence[1]);

        // Seed the isolation tenant through the primary client. The two independent clients below
        // then race on the same tenant-local normalized identity.
        var secondaryIsolationSecret = CreateSecret(
            "secret-beta-shared",
            SecondaryTenantId,
            WinnerSecretName,
            "tenant-beta-value");
        Assert(await primary.TryAddAsync(secondaryIsolationSecret, cancellationToken), "seed-secondary-tenant");
        var createResult = await CreateContendersAsync(primary, secondary, cancellationToken);
        if (createResult.SuccessCount != 1)
            throw new InvalidOperationException("The Secret workload did not enforce exactly one tenant-local create winner.");

        observableResults["concurrent-create-success-count"] = createResult.SuccessCount.ToString();
        observableResults["create-winner-id"] = WinnerSecretId;
        operations.Add(OperationSequence[2]);

        var winner = await primary.FindAsync(PrimaryTenantId, WinnerSecretName, cancellationToken)
            ?? throw new InvalidOperationException("The Secret workload could not read the accepted create winner.");
        var version = winner.LatestActiveVersion
            ?? throw new InvalidOperationException("The Secret workload winner has no active version.");
        if (winner.Id != WinnerSecretId || version.Version != 1 || version.Payload.Value != WinnerSecretValue)
            throw new InvalidOperationException("The Secret workload point read did not return the exact create winner value and version.");

        observableResults["read-winner-id"] = winner.Id;
        observableResults["read-winner-value"] = version.Payload.Value ?? string.Empty;
        observableResults["read-winner-version"] = version.Version.ToString();
        operations.Add(OperationSequence[3]);

        var firstPage = await primary.ListPageAsync(PrimaryTenantId, Page(0), cancellationToken);
        var nextPage = await primary.ListPageAsync(PrimaryTenantId, Page(PageSize), cancellationToken);
        AssertPage(firstPage, nextPage, expectedTotal: CanonicalSecretCount + NoiseSecretCount + 1);
        var betaPage = await secondary.ListPageAsync(SecondaryTenantId, Page(0), cancellationToken);
        if (betaPage.Items.Count != 1 || betaPage.Items[0].TenantId != SecondaryTenantId)
            throw new InvalidOperationException("The Secret workload secondary tenant list was not isolated.");

        observableResults["first-page-count"] = firstPage.Items.Count.ToString();
        observableResults["first-page-identity-digest"] = IdentityDigest(firstPage.Items);
        observableResults["next-page-count"] = nextPage.Items.Count.ToString();
        observableResults["next-page-identity-digest"] = IdentityDigest(nextPage.Items);
        observableResults["total-count"] = firstPage.TotalCount.ToString();
        observableResults["cross-tenant-result-count"] = firstPage.Items.Concat(nextPage.Items)
            .Count(secret => secret.TenantId != PrimaryTenantId)
            .ToString();
        observableResults["secondary-tenant-result-count"] = betaPage.Items.Count.ToString();
        operations.Add(OperationSequence[4]);
        operations.Add(OperationSequence[5]);

        var digest = ComputeResultDigest(operations, observableResults);
        if (digest != ExpectedResultDigest)
            throw new InvalidOperationException(
                $"The Secret workload observations no longer match the frozen result digest (observed '{digest}').");

        return new SecretCreateReadListResult(
            ComputeInputFingerprint(),
            digest,
            operations,
            observableResults);
    }

    /// <summary>
    /// Returns the point-read and two bounded list routes. Fixture creation is deliberately performed by
    /// <see cref="ExecuteAsync"/> and is therefore outside process timing.
    /// </summary>
    public async ValueTask<IReadOnlyList<ISecretCreateReadListWorkloadOperation>> PrepareMeasuredOperationsAsync(
        ISecretCreateReadListWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ValidateFrozenContract();
        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        var primary = scopes.Primary;
        return
        [
            new SecretCreateReadListWorkloadOperation(
                OperationSequence[3],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var winner = await primary.FindAsync(PrimaryTenantId, WinnerSecretName, token)
                        ?? throw new InvalidOperationException("Measured Secret point read returned no winner.");
                    var version = winner.LatestActiveVersion;
                    if (winner.Id != WinnerSecretId || version?.Version != 1 || version.Payload.Value != WinnerSecretValue)
                        throw new InvalidOperationException("Measured Secret point read returned the wrong winner value or version.");
                }),
            new SecretCreateReadListWorkloadOperation(
                OperationSequence[4],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await primary.ListPageAsync(PrimaryTenantId, Page(0), token);
                    if (page.Items.Count != PageSize || page.TotalCount != CanonicalSecretCount + NoiseSecretCount + 1)
                        throw new InvalidOperationException("Measured Secret first page was not the frozen bounded result.");
                }),
            new SecretCreateReadListWorkloadOperation(
                OperationSequence[5],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await primary.ListPageAsync(PrimaryTenantId, Page(PageSize), token);
                    if (page.Items.Count != PageSize || page.TotalCount != CanonicalSecretCount + NoiseSecretCount + 1)
                        throw new InvalidOperationException("Measured Secret next page was not the frozen bounded result.");
                })
        ];
    }

    public static IEnumerable<Secret> CanonicalSecrets(string tenantId)
    {
        for (var index = 0; index < CanonicalSecretCount; index++)
        {
            var name = $"canonical-{index:D3}";
            var value = index switch
            {
                0 => "equal-canonical-value",
                1 => "equal-canonical-value",
                _ => "秘密-π-value"
            };
            var secret = CreateSecret($"secret-canonical-{index:D3}", tenantId, name, value);
            if (index == 2)
                secret.Description = new string('d', 1024);
            yield return secret;
        }
    }

    public static IEnumerable<Secret> NoiseSecrets(string tenantId)
    {
        for (var index = 0; index < NoiseSecretCount; index++)
        {
            var name = $"noise-{index:D4}";
            var secret = CreateSecret(
                $"secret-noise-{index:D4}",
                tenantId,
                name,
                index switch
                {
                    0 => null,
                    1 => new string('x', 2048),
                    _ => "equal-canonical-value"
                });
            if (index == 2)
                secret.Status = SecretStatus.Retired;
            if (index == 3)
                secret.Status = SecretStatus.Deleted;
            yield return secret;
        }
    }

    public static Secret CreateSecret(string id, string tenantId, string normalizedName, string? value) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = normalizedName,
        DisplayName = normalizedName,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        CreatedAt = FixtureNow,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                CreatedAt = FixtureNow,
                Payload = SecretPayload.FromValue(value)
            }
        ]
    };

    public static SecretRepositoryListRequest Page(int skip) =>
        new(skip: skip, take: PageSize);

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
        }, CanonicalJsonOptions));

    private static async ValueTask<ContenderResult> CreateContendersAsync(
        ISecretCreateReadListClient primary,
        ISecretCreateReadListClient secondary,
        CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        async Task<bool> AttemptAsync(ISecretCreateReadListClient client)
        {
            if (Interlocked.Increment(ref readyCount) == ConcurrentContenders)
                ready.TrySetResult(true);
            await start.Task.WaitAsync(cancellationToken);
            // Keep both public repository calls live at the same time. The clients are independent
            // scopes, while the payload is identical so the accepted winner remains deterministic
            // without imposing an artificial winner ordering on the database race.
            return await Task.Run(
                async () => await client.TryAddAsync(
                    CreateSecret(WinnerSecretId, PrimaryTenantId, WinnerSecretName, WinnerSecretValue),
                    cancellationToken),
                cancellationToken);
        }

        var winnerTask = AttemptAsync(primary);
        var loserTask = AttemptAsync(secondary);
        await ready.Task.WaitAsync(cancellationToken);
        start.SetResult(true);
        var outcomes = await Task.WhenAll(winnerTask, loserTask);
        return new ContenderResult(outcomes.Count(outcome => outcome));
    }

    private static void Assert(bool condition, string operation)
    {
        if (!condition)
            throw new InvalidOperationException($"Secret workload operation '{operation}' was not accepted.");
    }

    private static void AssertPage(SecretRepositoryPage firstPage, SecretRepositoryPage nextPage, long expectedTotal)
    {
        if (firstPage.TotalCount != expectedTotal || nextPage.TotalCount != expectedTotal ||
            firstPage.Items.Count != PageSize || nextPage.Items.Count != PageSize ||
            firstPage.Items.Any(secret => secret.TenantId != PrimaryTenantId) ||
            nextPage.Items.Any(secret => secret.TenantId != PrimaryTenantId))
            throw new InvalidOperationException("The Secret workload list pages were not bounded to the requested tenant and size.");

        if (!firstPage.Items.Concat(nextPage.Items).Select(secret => secret.Name).SequenceEqual(
                firstPage.Items.Concat(nextPage.Items).Select(secret => secret.Name).Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
            throw new InvalidOperationException("The Secret workload list pages were not deterministically ordered.");

        if (firstPage.Items.Select(secret => secret.Id).Intersect(nextPage.Items.Select(secret => secret.Id), StringComparer.Ordinal).Any())
            throw new InvalidOperationException("The Secret workload offset pages overlap.");
    }

    private static string IdentityDigest(IReadOnlyList<Secret> page) =>
        Hash(JsonSerializer.Serialize(page.Select(secret => new { secret.Id, secret.TenantId, secret.Name }), CanonicalJsonOptions));

    private static void ValidateFrozenContract()
    {
        if (OperationSequence.Count != 6 || OperationSequence[2] != "concurrent-create-same-secret" ||
            ComputeInputFingerprint() != ExpectedInputFingerprint)
            throw new InvalidOperationException("The Secret workload definition no longer matches its frozen contract.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record ContenderResult(int SuccessCount);
}

public interface ISecretCreateReadListWorkloadAdapter
{
    ValueTask<SecretCreateReadListScopes> OpenIsolatedScopesAsync(CancellationToken cancellationToken = default);
}

public sealed record SecretCreateReadListScopes(
    ISecretCreateReadListClient Primary,
    ISecretCreateReadListClient Secondary);

public interface ISecretCreateReadListClient
{
    ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default);
    ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default);
    ValueTask<SecretRepositoryPage> ListPageAsync(string tenantId, SecretRepositoryListRequest request, CancellationToken cancellationToken = default);
}

public interface ISecretCreateReadListWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class SecretCreateReadListWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : ISecretCreateReadListWorkloadOperation
{
    public string Id { get; } = id;

    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) =>
        prepare(invocation, cancellationToken);

    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) =>
        invoke(invocation, cancellationToken);
}

public sealed record SecretCreateReadListResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, string> ObservableResults);
