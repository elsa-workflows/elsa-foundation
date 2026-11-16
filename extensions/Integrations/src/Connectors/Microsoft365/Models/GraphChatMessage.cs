namespace Elsa.Integrations.Connectors.Microsoft365.Models;

public sealed record GraphChatMessage
{
    public string? Id { get; init; }

    public string? WebUrl { get; init; }
}
