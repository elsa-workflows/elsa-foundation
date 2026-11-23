namespace Elsa.Api.AspNetCore;

/// <summary>
/// Records the server-side filter that enforces a host credential when the
/// filter is not represented by ASP.NET Core's authorization metadata.
/// </summary>
public sealed record EndpointHostCredentialEnforcementMetadata
{
    public EndpointHostCredentialEnforcementMetadata(string credential, string owner)
    {
        if (string.IsNullOrWhiteSpace(credential))
            throw new ArgumentException("A host credential is required.", nameof(credential));
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("A host owner is required.", nameof(owner));

        Credential = credential.Trim();
        Owner = owner.Trim();
    }

    public string Credential { get; }
    public string Owner { get; }
}
