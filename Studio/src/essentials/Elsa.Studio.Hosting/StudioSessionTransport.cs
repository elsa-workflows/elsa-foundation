namespace Elsa.Studio.Hosting;

/// <summary>Uses the root HTTP factory for bearer introspection without replacing shell-owned module factories.</summary>
internal sealed class StudioSessionTransport(IHttpClientFactory factory)
{
    public HttpClient CreateClient() => factory.CreateClient(StudioBridgeAuthHandler.HttpClientName);
}
