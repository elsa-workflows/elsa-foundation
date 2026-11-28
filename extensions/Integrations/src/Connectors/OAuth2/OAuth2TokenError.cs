using System.Net;
using System.Text.Json;

namespace Elsa.Integrations.Connectors.OAuth2;

/// <summary>
/// Surfaces a token-endpoint failure that has no <c>access_token</c>. Only the provider's
/// <c>error</c> and <c>error_description</c> strings are copied, each truncated, so a body that
/// echoes a client secret, code, or token cannot leak through the exception.
/// </summary>
internal static class OAuth2TokenError
{
    public const int MaxFieldLength = 200;
    public const string InvalidGrant = "invalid_grant";

    public static string MissingAccessTokenMessage(JsonElement root)
    {
        var error = ReadTrustedField(root, "error");
        var description = ReadTrustedField(root, "error_description");
        if (error is null && description is null)
            return "Token endpoint returned no access_token.";

        var detail = description is null ? error : error is null ? description : $"{error}: {description}";
        return $"Token endpoint returned no access_token ({detail}).";
    }

    public static string? TryReadError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return ReadTrustedField(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsPermanentGrantLoss(string? errorCode) =>
        string.Equals(errorCode, InvalidGrant, StringComparison.OrdinalIgnoreCase);

    public static bool IsTransientStatus(HttpStatusCode status) => (int)status >= 500;

    public static string? ReadError(JsonElement root) => ReadTrustedField(root, "error");

    private static string? ReadTrustedField(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return null;

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        return value.Length <= MaxFieldLength ? value : value[..MaxFieldLength];
    }
}
