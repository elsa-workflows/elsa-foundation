namespace Elsa.Integrations.Connectors.Slack.Models;

public sealed record SlackMessageResult
{
    public string? Channel { get; init; }

    public string? Ts { get; init; }
}
