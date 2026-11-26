using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class ConnectionRepositoryCapabilitiesTests
{
    [Fact]
    public async Task The_default_repository_identifies_its_disposable_storage()
    {
        await using var provider = ConnectionsTestComposition.CreateProvider();
        var capabilities = await provider.GetRequiredService<IConnectionRepository>().GetCapabilitiesAsync();
        Assert.Equal("in-memory", capabilities.Provider);
        Assert.False(capabilities.IsDurable);
    }

    [Fact]
    public async Task An_existing_contribution_cannot_silently_claim_durability()
    {
        IConnectionRepository repository = new LegacyRepository();
        var capabilities = await repository.GetCapabilitiesAsync();
        Assert.Equal("unspecified", capabilities.Provider);
        Assert.False(capabilities.IsDurable);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await repository.GetCapabilitiesAsync(cancellation.Token));
    }

    private sealed class LegacyRepository : IConnectionRepository
    {
        public ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> IsSecretReferencedAsync(string tenantId, string secretName, string? excludedConnectionName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string tenantId, string name, int expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
