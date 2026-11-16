namespace Elsa.Integrations.Connectors.Microsoft365.Models;

public sealed record GraphEvent
{
    public string? Id { get; init; }

    public string? Subject { get; init; }

    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public string? TimeZone { get; init; }

    public string? Location { get; init; }

    public string? WebLink { get; init; }
}
