namespace Elsa.Activities.Http.Connections;

/// <summary>
/// Options an <see cref="IHttpConnectionApplicator"/> may set on an outbound request.
/// A bound connection that receives 401 can ask the applicator to refresh credentials and retry.
/// Authenticators may also queue extra secret values for <c>IRuntimeSecretMask</c>.
/// </summary>
public static class HttpConnectionApplyOptions
{
    public static readonly HttpRequestOptionsKey<bool> RefreshCredentials = new("Elsa.Http.RefreshConnectionCredentials");

    public static readonly HttpRequestOptionsKey<List<(string Name, string Value)>> MaskedSecrets = new("Elsa.Http.MaskedConnectionSecrets");

    public static void MaskSecret(HttpRequestMessage request, string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(name))
            return;

        if (!request.Options.TryGetValue(MaskedSecrets, out var list) || list is null)
        {
            list = [];
            request.Options.Set(MaskedSecrets, list);
        }

        list.Add((name, value));
    }
}
