namespace Elsa.Secrets.Core.Models;

public sealed class ResolvedSecret
{
    public bool Succeeded { get; init; }
    public string? Value { get; init; }
    public SecretResolutionFailureCode FailureCode { get; init; }
    public string? Error { get; init; }
    public SecretMetadata? Metadata { get; init; }

    public static ResolvedSecret Success(string value, SecretMetadata metadata) => new()
    {
        Succeeded = true,
        Value = value,
        Metadata = metadata
    };

    public static ResolvedSecret Failure(SecretResolutionFailureCode code, string error) => new()
    {
        Succeeded = false,
        FailureCode = code,
        Error = error
    };
}
