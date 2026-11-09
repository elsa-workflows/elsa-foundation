namespace Elsa.Integrations.Connectors.Slack;

internal sealed class SlackApiException : Exception
{
    public SlackApiException(string code, string message, bool isRetryable = false, string? needed = null)
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
