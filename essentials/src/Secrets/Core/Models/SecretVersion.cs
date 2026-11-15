namespace Elsa.Secrets.Core.Models;

public sealed class SecretVersion
{
    public int Version { get; set; }
    public SecretStatus Status { get; set; } = SecretStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UnixEpoch;
    public DateTimeOffset? ExpiresAt { get; set; }
    public SecretPayload Payload { get; set; } = new();

    public bool IsExpired(DateTimeOffset? now = null)
    {
        if (Status == SecretStatus.Expired)
            return true;

        return ExpiresAt is not null && ExpiresAt <= (now ?? DateTimeOffset.UtcNow);
    }
}
