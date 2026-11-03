namespace Elsa.Activities.Http.Connections;

/// <summary>
/// Options an <see cref="IHttpConnectionApplicator"/> may set on an outbound request.
/// A bound connection whose scheme has refreshable state can ask the applicator to refresh
/// credentials and retry after 401. Authenticators queue extra secret values for
/// <c>IRuntimeSecretMask</c> through <see cref="MaskSecret"/>.
/// </summary>
public static class HttpConnectionApplyOptions
{
    public static readonly HttpRequestOptionsKey<bool> RefreshCredentials = new("Elsa.Http.RefreshConnectionCredentials");

    public static readonly HttpRequestOptionsKey<bool> HasRefreshableState = new("Elsa.Http.HasRefreshableState");

    internal static readonly HttpRequestOptionsKey<List<(string Name, string Value)>> MaskedSecrets = new("Elsa.Http.MaskedConnectionSecrets");

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

    public static IReadOnlyList<(string Name, string Value)> GetMaskedSecrets(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(MaskedSecrets, out var list) && list is not null
            ? list
            : [];
    }
}
