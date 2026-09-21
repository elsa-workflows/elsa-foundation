namespace Elsa.Secrets.Api.Requests;

public sealed class RotateSecretApiRequest
{
    public string Name { get; set; } = "";
    public string? Value { get; set; }
    public string? ConfigurationKey { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
