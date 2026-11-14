namespace Kernel.Native;

// Private ordinary recovery control; not chart input or a product operator API.
internal sealed record NativeRetry(string Id, string Source, string Incident, string Reason)
{
    internal const string Kind = "native.retry-intervention";
}
