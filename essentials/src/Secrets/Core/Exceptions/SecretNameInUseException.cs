namespace Elsa.Secrets.Core.Exceptions;

/// <summary>
/// Thrown when a create names a secret that a secret which is not deleted already holds in the tenant. A deleted
/// secret frees its name, so the holder is live (it may be revoked or expired). It derives from
/// <see cref="InvalidOperationException"/>, which Secrets threw here before this type existed, so existing handlers
/// keep catching it.
/// </summary>
public sealed class SecretNameInUseException : InvalidOperationException
{
    public SecretNameInUseException(string secretName)
        : base("A secret with the same name already exists.")
    {
        SecretName = secretName;
    }

    /// <summary>The normalized name that is in use.</summary>
    public string SecretName { get; }
}
