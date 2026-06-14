namespace Elsa.Studio.ConsoleStream.Options;

public sealed class ConsoleStreamStudioOptions
{
    public string EndpointPrefix { get; set; } = "/_elsa/studio/diagnostics/console-logs";

    public int RecentCapacity { get; set; } = 2_000;

    public int MaxRecentQuerySize { get; set; } = 2_000;

    public bool PreserveAnsi { get; set; } = true;
}
