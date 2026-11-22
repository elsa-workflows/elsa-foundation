namespace Elsa.Secrets.Core.Exceptions;

/// <summary>
/// Thrown when a request names a secret store or secret type that is not registered, or a store that the secret type
/// does not support. The fault is in the host's Secrets configuration or the request, not a transient store outage, so
/// retrying the same request does not help. It derives from <see cref="InvalidOperationException"/>, which Secrets
/// threw here before this type existed, so existing handlers keep catching it.
/// </summary>
public sealed class SecretStoreConfigurationException : InvalidOperationException
{
    public SecretStoreConfigurationException(string message)
        : base(message)
    {
    }
}
