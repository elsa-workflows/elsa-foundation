using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class SecretCreateReadListWorkloadTests
{
    [Fact]
    public async Task Executes_the_tenant_isolated_winner_and_bounded_pages()
    {
        var adapter = new DictionarySecretAdapter();

        var result = await new SecretCreateReadListWorkload().ExecuteAsync(adapter);

        Assert.Equal(SecretCreateReadListWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(SecretCreateReadListWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(SecretCreateReadListWorkload.OperationSequence, result.ObservableOperations);
        Assert.Equal(SecretCreateReadListWorkload.CanonicalSecretCount + SecretCreateReadListWorkload.NoiseSecretCount + 1, adapter.Primary.CountForTenant(SecretCreateReadListWorkload.PrimaryTenantId));
        Assert.Equal(1, adapter.Secondary.CountForTenant(SecretCreateReadListWorkload.SecondaryTenantId));
        Assert.Equal("secret-winner-value", result.ObservableResults["read-winner-value"]);
        Assert.Equal("1", result.ObservableResults["read-winner-version"]);
        Assert.Equal("68", result.ObservableResults["total-count"]);
    }

    [Fact]
    public async Task Leaves_fixture_writes_outside_the_three_measured_public_routes()
    {
        var adapter = new DictionarySecretAdapter();
        await new SecretCreateReadListWorkload().ExecuteAsync(adapter);
        var operations = await new SecretCreateReadListWorkload().PrepareMeasuredOperationsAsync(adapter);

        Assert.Equal(
            [
                "read-create-winner-by-identity",
                "list-secrets-bounded-first-page",
                "list-secrets-bounded-next-offset-page"
            ],
            operations.Select(operation => operation.Id));

        foreach (var operation in operations)
        {
            await operation.PrepareInvocationAsync(0);
            await operation.InvokeAsync(0);
        }
    }

    [Fact]
    public async Task Releases_two_independent_same_tenant_create_calls_concurrently()
    {
        var overlap = new ContenderOverlapProbe();
        var adapter = new DictionarySecretAdapter(overlap);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await new SecretCreateReadListWorkload().ExecuteAsync(adapter, timeout.Token);

        Assert.Equal(SecretCreateReadListWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(2, overlap.MaxConcurrent);
    }

    [Fact]
    public void Public_workload_surface_contains_no_EF_or_connection_types()
    {
        var surface = new[]
        {
            typeof(ISecretCreateReadListWorkloadAdapter),
            typeof(ISecretCreateReadListClient),
            typeof(SecretCreateReadListScopes)
        }
        .SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        .Select(member => member.ToString()!)
        .Append(typeof(ISecretCreateReadListWorkloadAdapter).ToString());

        Assert.DoesNotContain(surface, value =>
            value.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("DbContext", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DictionarySecretAdapter : ISecretCreateReadListWorkloadAdapter
    {
        public DictionarySecretStore Primary { get; }
        public DictionarySecretStore Secondary { get; }

        public DictionarySecretAdapter(ContenderOverlapProbe? overlap = null)
        {
            var state = new DictionarySecretState(overlap);
            Primary = new DictionarySecretStore(state);
            Secondary = new DictionarySecretStore(state);
        }

        public ValueTask<SecretCreateReadListScopes> OpenIsolatedScopesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(new SecretCreateReadListScopes(Primary, Secondary));
        }
    }

    private sealed class DictionarySecretStore : ISecretCreateReadListClient
    {
        private readonly DictionarySecretState state;

        public DictionarySecretStore(DictionarySecretState state) => this.state = state;

        public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecretNameConstraints.Validate(secret.Name);
            var enteredProbe = false;
            if (state.Overlap is not null && secret.Id == SecretCreateReadListWorkload.WinnerSecretId)
            {
                await state.Overlap.EnterAsync(cancellationToken);
                enteredProbe = true;
            }

            try
            {
                lock (state.Gate)
                {
                    var key = (secret.TenantId, secret.Name);
                    if (state.Secrets.ContainsKey(key))
                        return false;
                    state.Secrets[key] = secret;
                    return true;
                }
            }
            finally
            {
                if (enteredProbe)
                    state.Overlap!.Exit();
            }
        }

        public ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (state.Gate)
                return new(state.Secrets.GetValueOrDefault((tenantId, normalizedName)));
        }

        public ValueTask<SecretRepositoryPage> ListPageAsync(string tenantId, SecretRepositoryListRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Secret[] candidates;
            lock (state.Gate)
            {
                candidates = state.Secrets
                    .Where(pair => pair.Key.TenantId == tenantId)
                    .Select(pair => pair.Value)
                    .OrderBy(secret => secret.Name, StringComparer.Ordinal)
                    .ThenBy(secret => secret.Id, StringComparer.Ordinal)
                    .ToArray();
            }
            return new(new SecretRepositoryPage(candidates.Skip(request.Skip).Take(request.Take).ToArray(), candidates.Length));
        }

        public int CountForTenant(string tenantId)
        {
            lock (state.Gate)
                return state.Secrets.Keys.Count(key => key.TenantId == tenantId);
        }

    }

    private sealed class DictionarySecretState(ContenderOverlapProbe? overlap)
    {
        public object Gate { get; } = new();
        public Dictionary<(string TenantId, string Name), Secret> Secrets { get; } = new();
        public ContenderOverlapProbe? Overlap { get; } = overlap;
    }

    private sealed class ContenderOverlapProbe
    {
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int active;
        private int maxConcurrent;

        public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

        public async ValueTask EnterAsync(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            while (true)
            {
                var previous = Volatile.Read(ref maxConcurrent);
                if (previous >= current || Interlocked.CompareExchange(ref maxConcurrent, current, previous) == previous)
                    break;
            }

            if (current == SecretCreateReadListWorkload.ConcurrentContenders)
                release.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
        }

        public void Exit() => Interlocked.Decrement(ref active);
    }
}
