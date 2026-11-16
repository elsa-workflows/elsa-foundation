using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;

// Owned qualification host only. No production registration or fault switch is exposed.
internal sealed class ProbeSessionFaults
{
    private Fault? armed;
    public bool Arm(string sessionId, string mode) =>
        Interlocked.CompareExchange(ref armed, new(sessionId, mode), null) is null;

    public string? Consume(string tenantId, string sessionId)
    {
        var current = Volatile.Read(ref armed);
        if (current is null || tenantId != "default" || current.SessionId != sessionId) return null;
        return ReferenceEquals(Interlocked.CompareExchange(ref armed, null, current), current) ? current.Mode : null;
    }

    private sealed record Fault(string SessionId, string Mode);
}

internal sealed class ProbeSessionStore(IAuthenticationSessionStore inner, ProbeSessionFaults faults) : IAuthenticationSessionStore
{
    public bool IsDurable => inner.IsDurable;
    public ValueTask<bool> TryCreateAsync(AuthenticationSessionRecord session, CancellationToken cancellationToken = default) => inner.TryCreateAsync(session, cancellationToken);
    public ValueTask<AuthenticationSessionRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default) => inner.FindAsync(tenantId, id, cancellationToken);
    public ValueTask<IamPage<AuthenticationSessionRecord>> ListAsync(string tenantId, string userId, IamPageRequest page, CancellationToken cancellationToken = default) => inner.ListAsync(tenantId, userId, page, cancellationToken);

    public async ValueTask<AuthenticationSessionRevocationStatus> RevokeAsync(string tenantId, string id, string expectedRevision, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mode = faults.Consume(tenantId, id);
        if (mode == "before-write") throw Failure();
        var result = await inner.RevokeAsync(tenantId, id, expectedRevision, now, cancellationToken);
        if (mode == "after-commit" && result == AuthenticationSessionRevocationStatus.Revoked) throw Failure();
        return result;

        AuthenticationStoreException Failure() => new("revoke", tenantId, id, new IOException("Owned qualification fault."));
    }
}
