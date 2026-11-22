namespace Elsa.Integrations.Connectors.Microsoft365.Models;

public sealed record GraphDriveItem
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public long? Size { get; init; }

    public string? WebUrl { get; init; }
}

public sealed record GraphFileDownload
{
    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public byte[] Content { get; init; } = [];
}
