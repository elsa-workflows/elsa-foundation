using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkStoreSessionFactoryTests
{
    [Fact]
    public async Task Missing_publication_fails_readiness_before_session_acquisition()
    {
        var sessions = new StubSessionFactory();
        var factory = new OpenIddictGroundworkStoreSessionFactory(new GroundworkStoreSessionSource(), sessions);

        await Assert.ThrowsAsync<OpenIddictGroundworkReadinessException>(() => factory.CreateAsync().AsTask());

        Assert.Equal(0, sessions.OpenCount);
    }

    [Fact]
    public async Task Legacy_publication_without_cross_unit_atomic_evidence_fails_readiness()
    {
        var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySet((_, _) => throw new InvalidOperationException("must not open")));
        var sessions = new StubSessionFactory();
        var factory = new OpenIddictGroundworkStoreSessionFactory(source, sessions);

        await Assert.ThrowsAsync<OpenIddictGroundworkReadinessException>(() => factory.CreateAsync().AsTask());

        Assert.Equal(0, sessions.OpenCount);
    }

    [Fact]
    public async Task Admitted_publication_opens_only_the_ordinary_global_session()
    {
        var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySetAdmitted(
            (_, _) => throw new InvalidOperationException("the wrapper must use the registered session factory"),
            TransactionBoundary.CrossUnitAtomic));
        var expected = new StubSessionFactory.SessionMarkerException();
        var sessions = new StubSessionFactory(expected);
        var factory = new OpenIddictGroundworkStoreSessionFactory(source, sessions);

        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkProviderException>(() => factory.CreateAsync().AsTask());

        Assert.Same(expected, exception.InnerException);
        Assert.Equal("session.open", exception.Operation);
        Assert.Equal(1, sessions.OpenCount);
        Assert.Null(sessions.RequiredPolicy);
    }

    [Fact]
    public async Task Cancellation_is_preserved_after_admission()
    {
        var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySetAdmitted(
            (_, _) => throw new InvalidOperationException("must not open"),
            TransactionBoundary.CrossUnitAtomic));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sessions = new StubSessionFactory();
        var factory = new OpenIddictGroundworkStoreSessionFactory(source, sessions);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateAsync(cancellation.Token).AsTask());

        Assert.Equal(0, sessions.OpenCount);
    }

    private sealed class StubSessionFactory(Exception? openFailure = null) : IGroundworkStoreSessionFactory
    {
        public int OpenCount { get; private set; }
        public PersistenceAccessPolicy? RequiredPolicy { get; private set; }

        public ValueTask<GroundworkStoreSession> CreateAsync(
            PersistenceAccessPolicy requiredPolicy,
            CancellationToken cancellationToken = default)
        {
            RequiredPolicy = requiredPolicy;
            throw new InvalidOperationException("OpenIddict must not request a scoped or privileged session.");
        }

        public ValueTask<GroundworkStoreSession> CreateOrdinaryGlobalAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            throw openFailure ?? new InvalidOperationException("No test session was configured.");
        }

        public ValueTask<TResult> ExecutePrivilegedAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<TResult> ExecutePrivilegedAcrossScopesAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public sealed class SessionMarkerException : Exception;
    }
}
