using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BridgeTestHostContentRootCollection
{
    public const string Name = "bridge-test-host-content-root";
}

[Collection(BridgeTestHostContentRootCollection.Name)]
public sealed class BridgeTestHostContentRootTests
{
    [Fact]
    public void CreateBuilderUsesStableContentRootWhenCurrentDirectoryIsTemporary()
    {
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var alternateDirectory = Directory.CreateTempSubdirectory();
        var alternatePath = Path.GetFullPath(alternateDirectory.FullName);
        var resources = new List<IDisposable>();

        try
        {
            var expectedContentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            Assert.NotEqual(expectedContentRoot, alternatePath);
            Directory.SetCurrentDirectory(alternatePath);

            var defaultBuilder = WebApplication.CreateSlimBuilder();
            resources.Add(defaultBuilder.Configuration);
            if (defaultBuilder.Environment.ContentRootFileProvider is IDisposable defaultFiles) resources.Add(defaultFiles);
            Assert.Equal(ResolvePhysicalPath(alternatePath), ResolvePhysicalPath(defaultBuilder.Environment.ContentRootPath));

            var bridgeBuilder = BridgeTestHost.CreateBuilder();
            resources.Add(bridgeBuilder.Configuration);
            if (bridgeBuilder.Environment.ContentRootFileProvider is IDisposable bridgeFiles) resources.Add(bridgeFiles);
            var actualContentRoot = bridgeBuilder.Environment.ContentRootPath;
            // Hosting may resolve a symlinked parent (for example, /var to /private/var on macOS).
            Assert.Equal(ResolvePhysicalPath(expectedContentRoot), ResolvePhysicalPath(actualContentRoot));
        }
        finally
        {
            try
            {
                Directory.SetCurrentDirectory(originalCurrentDirectory);
            }
            finally
            {
                try
                {
                    foreach (var resource in resources)
                        resource.Dispose();
                }
                finally
                {
                    alternateDirectory.Delete(recursive: true);
                }
            }
        }

        Assert.Throws<DirectoryNotFoundException>(() => WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = alternatePath
        }));
    }

    private static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var currentPath = root;

        var segments = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var candidatePath = Path.Combine(currentPath, segment);
            currentPath = new DirectoryInfo(candidatePath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidatePath;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentPath));
    }
}
