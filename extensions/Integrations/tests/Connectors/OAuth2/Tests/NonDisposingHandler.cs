namespace Elsa.Integrations.Connectors.OAuth2.Tests;

internal sealed class NonDisposingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override void Dispose(bool disposing)
    {
    }
}
