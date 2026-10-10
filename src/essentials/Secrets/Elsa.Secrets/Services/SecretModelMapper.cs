using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class SecretModelMapper
{
    public SecretMetadata Map(Secret secret)
    {
        var currentVersion = secret.LatestActiveVersion;

        return new()
        {
            Id = secret.Id,
            TenantId = secret.TenantId,
            Name = secret.Name,
            DisplayName = secret.DisplayName,
            Description = secret.Description,
            TypeName = secret.TypeName,
            StoreName = secret.StoreName,
            Scope = secret.Scope,
            Tags = secret.Tags.ToArray(),
            Status = secret.Status,
            CurrentVersion = currentVersion?.Version,
            CreatedAt = secret.CreatedAt,
            UpdatedAt = secret.UpdatedAt,
            ExpiresAt = currentVersion?.ExpiresAt
        };
    }
}
