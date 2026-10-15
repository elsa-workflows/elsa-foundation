namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Binds the named HTTP client's auth handler to the handle currently in use. Not a D1 shim for
/// <c>SendHttpRequest</c>: overlapping handles are not supported as concurrent users of one client;
/// use <c>await using</c> so dispose restores the previous handle.
/// </summary>
public static class ConnectionExecutionScope
{
    private static readonly AsyncLocal<IConnectionHandle?> Current = new();

    public static IConnectionHandle? CurrentHandle => Current.Value;

    public static IDisposable Push(IConnectionHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var previous = Current.Value;
        Current.Value = handle;
        return new Pop(previous);
    }

    private sealed class Pop(IConnectionHandle? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
