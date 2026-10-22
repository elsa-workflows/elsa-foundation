namespace Elsa.Activities.Http.Connections;

/// <summary>
/// Options an <see cref="IHttpConnectionApplicator"/> may set on an outbound request.
/// A bound connection that receives 401 can ask the applicator to refresh credentials and retry.
/// </summary>
public static class HttpConnectionApplyOptions
{
    public static readonly HttpRequestOptionsKey<bool> RefreshCredentials = new("Elsa.Http.RefreshConnectionCredentials");
}
