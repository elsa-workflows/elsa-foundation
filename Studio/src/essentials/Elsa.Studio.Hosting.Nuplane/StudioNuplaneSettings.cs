using global::Nuplane.Abstractions;
using global::Nuplane.Feeds.Configuration;
using global::Nuplane.Feeds.Setup;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed record StudioNuplaneSettings(string ManagementFile, string DirectoryFeedName, string DropFolder)
{
    public const string FallbackFeedName = "studio-hosting-fallback";
    public bool UsesFallback => DirectoryFeedName == FallbackFeedName;

    public static StudioNuplaneSettings Create(string managementFile, string? selected, FeedResolutionOptions options,
        IReadOnlySet<string> cacheOnlyFeeds)
    {
        var directories = WritableDirectories(options, cacheOnlyFeeds).ToArray();
        if (directories.Length == 0)
            throw new StudioHostingException("Studio administration requires a writable directory feed separate from configured cache-only feeds.");
        var candidates = directories.Where(feed => feed.Name != FallbackFeedName).ToArray();
        if (candidates.Length == 0) candidates = directories;
        var feed = selected is not null
            ? candidates.SingleOrDefault(candidate => string.Equals(candidate.Name, selected, StringComparison.OrdinalIgnoreCase))
                ?? throw new StudioHostingException($"DirectoryFeedName '{selected}' must identify a writable directory feed that resolves packages.")
            : candidates.Length == 1 ? candidates[0]
                : throw new StudioHostingException("Studio administration requires an explicit DirectoryFeedName when multiple writable directory feeds are registered.");
        return new(managementFile, feed.Name, Path.GetFullPath(feed.ServiceIndex.LocalPath));
    }

    public static IEnumerable<FeedDefinition> WritableDirectories(FeedResolutionOptions options, IReadOnlySet<string> cacheOnlyFeeds)
    {
        var directories = options.Feeds.Where(feed => feed.ServiceIndex.IsFile).ToArray();
        var pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var cachePaths = directories.Where(feed => cacheOnlyFeeds.Contains(feed.Name)).Select(DirectoryPath).ToHashSet(pathComparer);
        return directories.Where(feed => !cachePaths.Contains(DirectoryPath(feed)));
    }

    public static HashSet<string> ReadCacheOnlyFeedNames(IConfigurationSection nuplane) =>
        NuplaneFeedSetupDeclarationReader.Read(nuplane.GetSection("Setup")).Declarations
            .Where(feed => !string.IsNullOrWhiteSpace(feed.Options.DirectoryPath) && feed.Options.Directory.Role == DirectoryFeedRole.Cache)
            .Select(feed => feed.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsCacheRole(string? role) =>
        Enum.TryParse<DirectoryFeedRole>(role, ignoreCase: true, out var parsed) && parsed == DirectoryFeedRole.Cache;

    private static string DirectoryPath(FeedDefinition feed) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(feed.ServiceIndex.LocalPath));
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
