using System.Linq.Expressions;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US2 surface test (SC-001). A persisted version carries the author's full SemVer 2.0.0 string —
/// including prerelease + build metadata — and that exact string surfaces verbatim on every read
/// path: the <see cref="IActivityDefinitionVersion"/> read contract, the <see cref="ListDefinitionVersions"/>
/// projection/handler, and the API <see cref="ActivityDefinitionVersionDetailsView"/>.
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

    [Fact]
    public async Task ListProjection_ExposesAuthorSemverVerbatim()
    {
        var (version, _) = BuildPersistedVersion();
        var handler = new ListDefinitionVersionsRequestHandler(
            new InMemoryVersionStore([version]));

        var infos = (await handler.Handle(new ListDefinitionVersions(version.DefinitionId), CancellationToken.None)).ToList();

        var info = Assert.Single(infos);
        Assert.Equal(AuthorVersion, info.Version);
    }

    [Fact]
    public void DetailsView_ExposesAuthorSemverVerbatim()
    {
        var (version, _) = BuildPersistedVersion();

        var view = version.ToDetailsView();

        Assert.Equal(AuthorVersion, view.Version);
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
            DescriptorType = typeof(TypeInformation).FullName!,
        };

        return (version, definition);
    }

    /// <summary>
    /// Minimal in-memory <see cref="IActivityDefinitionVersionStore"/> that implements only the
    /// list-by-definition route the list handler uses; every other route fails loudly.
    /// </summary>
    private sealed class InMemoryVersionStore(List<ActivityDefinitionVersion> items) : IActivityDefinitionVersionStore
    {
        private const string Msg = "InMemoryVersionStore: only ListByDefinitionAsync is supported in this test.";

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>(items.Where(x => x.DefinitionId == definitionId).ToList());

        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    }
}
