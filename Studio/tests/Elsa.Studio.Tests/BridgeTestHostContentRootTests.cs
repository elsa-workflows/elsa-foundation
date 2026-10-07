using Microsoft.AspNetCore.Builder;

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
        var configurations = new List<IDisposable>();

        try
        {
            var expectedContentRoot = Path.GetFullPath(AppContext.BaseDirectory);
            Assert.NotEqual(expectedContentRoot, alternatePath);
            Directory.SetCurrentDirectory(alternatePath);

            var defaultBuilder = WebApplication.CreateSlimBuilder();
            configurations.Add(defaultBuilder.Configuration);
            Assert.Equal(alternatePath, Path.GetFullPath(defaultBuilder.Environment.ContentRootPath));

            var bridgeBuilder = BridgeTestHost.CreateBuilder();
            configurations.Add(bridgeBuilder.Configuration);
            Assert.Equal(expectedContentRoot, Path.GetFullPath(bridgeBuilder.Environment.ContentRootPath));
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
                    foreach (var configuration in configurations)
                        configuration.Dispose();
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
}
