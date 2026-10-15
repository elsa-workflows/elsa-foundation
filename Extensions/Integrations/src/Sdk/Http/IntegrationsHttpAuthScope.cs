namespace Elsa.Integrations.Sdk.Http;

/// <summary>
/// Caller-side scope that the named <c>Elsa.Activities.Http</c> client's
/// <see cref="IntegrationsHttpAuthHandler"/> reads. Must be pushed from the caller's method: an
/// async helper cannot publish <see cref="AsyncLocal{T}"/> to its caller.
/// </summary>
/// <remarks>
/// This is not a service-connection catalog, auth scheme, or egress policy. Those belong to
/// <c>Elsa.Connections</c> (#2729). When that lands, rebase this scope onto its resolved-connection
/// handle instead of stacking a second handler.
/// </remarks>
public static class IntegrationsHttpAuthScope
{
    private static readonly AsyncLocal<Action<HttpRequestMessage>?> Current = new();

    internal static Action<HttpRequestMessage>? CurrentApply => Current.Value;

    public static IDisposable Push(Action<HttpRequestMessage> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var previous = Current.Value;
        Current.Value = apply;
        return new Pop(previous);
    }

    private sealed class Pop(Action<HttpRequestMessage>? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
