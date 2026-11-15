namespace Elsa.Integrations.Connectors.Microsoft365;

internal sealed class Microsoft365ApiException : Exception
{
    public Microsoft365ApiException(string code, string message, bool isRetryable = false, string? needed = null)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        IsRetryable = isRetryable;
        Needed = needed;
    }

    public string Code { get; }

    public bool IsRetryable { get; }

    public string? Needed { get; }
}
