namespace Elsa.Integrations.Connectors.Microsoft365.Models;

public sealed record GraphRecipient
{
    public string? Address { get; init; }

    public string? Name { get; init; }
}

public sealed record GraphMessage
{
    public string? Id { get; init; }

    public string? Subject { get; init; }

    public string? BodyPreview { get; init; }

    public DateTimeOffset? ReceivedDateTime { get; init; }

    public bool IsRead { get; init; }

    public GraphRecipient? From { get; init; }
}
