using global::Nuplane.Feeds.Configuration;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed record StudioNuplaneSettings(string ManagementFile, string DirectoryFeedName, string DropFolder)
{
    public const string FallbackFeedName = "studio-hosting-fallback";
    public bool UsesFallback => DirectoryFeedName == FallbackFeedName;

    public static StudioNuplaneSettings Create(string managementFile, string? selected, FeedResolutionOptions options)
    {
        var directories = options.Feeds.Where(feed => feed.ServiceIndex.IsFile).ToArray();
        var candidates = directories.Where(feed => feed.Name != FallbackFeedName).ToArray();
        if (candidates.Length == 0) candidates = directories;
        var feed = selected is not null
            ? candidates.SingleOrDefault(candidate => string.Equals(candidate.Name, selected, StringComparison.OrdinalIgnoreCase))
                ?? throw new StudioHostingException($"DirectoryFeedName '{selected}' must identify a registered directory feed that resolves packages.")
            : candidates.Length == 1 ? candidates[0]
                : throw new StudioHostingException("Studio administration requires an explicit DirectoryFeedName when multiple directory feeds are registered.");
        return new(managementFile, feed.Name, Path.GetFullPath(feed.ServiceIndex.LocalPath));
    }
}

internal sealed class NuplaneHostingStartup(StudioNuplaneSettings settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = settings.DropFolder;
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
