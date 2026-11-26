namespace Elsa.Foundation.Identity.Core.Iam;

/// <summary>A managed external login: tenant and logical connection, plus exact validated issuer and opaque subject.</summary>
public sealed record ExternalIdentityKey
{
    public ExternalIdentityKey(string tenantId, string provider, string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        TenantId = tenantId;
        Provider = provider;
        Issuer = issuer;
        Subject = subject;
    }

    public string TenantId { get; }
    public string Provider { get; }
    public string Issuer { get; }
    public string Subject { get; }
}
