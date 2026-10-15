namespace Elsa.Integrations.Sdk.Harness;

/// <summary>Keeps a shared mock or test handler alive when a per-handle <see cref="HttpClient"/> is disposed.</summary>
internal sealed class NonDisposingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override void Dispose(bool disposing)
    {
        // The inner handler is owned by the harness.
    }
}
