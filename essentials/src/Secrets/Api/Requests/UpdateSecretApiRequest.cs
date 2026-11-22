namespace Elsa.Secrets.Api.Requests;

public sealed class UpdateSecretApiRequest
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}
