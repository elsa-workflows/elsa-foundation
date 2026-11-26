using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Version-string surface test. A persisted version carries the author's full SemVer 2.0.0 string,
/// including prerelease + build metadata, and that exact string surfaces verbatim on the
/// <see cref="IActivityDefinitionVersion"/> read contract. The authoring catalog's copy of it is
/// covered by <c>ActivityAuthoringCatalogVersionStringTests</c> in the Activities Design API tests.
/// </summary>
public sealed class VersionStringSurfaceTests
{
    private const string AuthorVersion = "1.2.3-rc.1+build.5";

    [Fact]
    public void ReadContract_ExposesAuthorSemverVerbatim()
    {
        var (version, _) = BuildPersistedVersion();

        Assert.Equal(AuthorVersion, ((IActivityDefinitionVersion)version).Version);
    }

    private static (ActivityDefinitionVersion Version, ActivityDefinition Definition) BuildPersistedVersion()
    {
        var definition = new ActivityDefinition
        {
            Id = "def-1",
            ActivityTypeKey = "Acme.Activities.Greet",
            Category = "Acme",
        };

        var version = new ActivityDefinitionVersion(AuthorVersion, definition.Id)
        {
            Definition = definition,
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
        };

        return (version, definition);
    }
}
