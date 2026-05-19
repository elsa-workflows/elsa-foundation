namespace Elsa.Server.Shells.Endpoints.Models;

internal class ShellReloadResponse
{
    public ShellReloadStatus Status { get; init; }
    public string? RequestedShellId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string? Message { get; init; }
}
