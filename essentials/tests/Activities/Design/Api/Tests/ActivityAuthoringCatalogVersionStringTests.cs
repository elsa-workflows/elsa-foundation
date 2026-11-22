using Elsa.Activities.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>
/// A persisted version carries the author's full SemVer 2.0.0 string, including prerelease and build metadata,
/// and the authoring catalog surfaces that exact string.
/// </summary>
public sealed class ActivityAuthoringCatalogVersionStringTests
{
    private const string AuthorVersion = "1.2.3-rc.1+build.5";

    [Fact]
    public async Task Catalog_exposes_the_author_semver_verbatim()
    {
        var definition = new ActivityDefinition { Id = "def-1", ActivityTypeKey = "Acme.Activities.Greet", Category = "Acme" };
        var version = new ActivityDefinitionVersion(AuthorVersion, definition.Id) { Id = "ver-1" };

        var view = await CatalogReaderFixture.ListAsync([definition], [version]);

        Assert.Equal(AuthorVersion, Assert.Single(view.Activities, activity => activity.ActivityTypeKey == definition.ActivityTypeKey).Version);
    }
}
