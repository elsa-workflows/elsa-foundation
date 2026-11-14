namespace Elsa.Etl.Core.Runtime;

/// <summary>A runtime storage operation failed without producing its requested outcome.</summary>
/// <remarks>The original failure remains in <see cref="Exception.InnerException"/>; the message contains no provider details.</remarks>
public sealed class EtlStoreException : Exception
{
    public EtlStoreException(string operation, EtlScope? scope, string? runId, Exception innerException)
        : base($"ETL store operation '{operation}' failed.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(innerException);
        Operation = operation;
        Scope = scope;
        RunId = runId;
    }

    public string Operation { get; }
    public EtlScope? Scope { get; }
    public string? RunId { get; }
}
