namespace Elsa.Secrets.Core.Exceptions;

/// <summary>
/// Thrown when a rotate specifies an expected version that is no longer current.
/// </summary>
public sealed class SecretVersionConflictException : InvalidOperationException
{
    public SecretVersionConflictException(string secretName, int? currentVersion)
        : base($"Secret '{secretName}' is no longer at the expected version.")
    {
        SecretName = secretName;
        CurrentVersion = currentVersion;
    }

    public string SecretName { get; }

    public int? CurrentVersion { get; }
}
