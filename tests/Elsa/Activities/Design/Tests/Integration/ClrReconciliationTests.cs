using System.Reflection;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Design.Reconciliation.Clr.Options;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Primitives.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// US1 independent test (FR-020, SC-003). Drives the real <see cref="ClrActivityReconciliationSource"/>
/// (with the real scanner + version resolver), the real <see cref="CollectActivityVersions"/>,
/// and the real <see cref="ActivityVersionReconciler"/> against an in-memory catalog. The fixture
/// assembly is versioned <c>2.1.0</c> and contains one un-annotated activity (→ assembly version) and
/// one <c>[Version("3.0.0")]</c> activity (override wins).
/// </summary>
public sealed class ClrReconciliationTests
{
    [Fact]
    public async Task FolderSource_ReconcilesAuthorControlledVersions_AndIsIdempotent()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var store = new InMemoryReconcilerHarness.CatalogStore();
        var reconciler = InMemoryReconcilerHarness.BuildReconciler(store, FolderSource(folder.Path));

        await reconciler.Reconcile(CancellationToken.None);

        Assert.Equal("2.1.0", VersionFor<UnannotatedFixtureActivity>(store));
        Assert.Equal("3.0.0", VersionFor<VersionedFixtureActivity>(store));
        // Inherited [Version] on a base class is honoured (issue #417 item 3): no own [Version], so 4.0.0
        // comes from VersionedBaseActivity rather than the assembly's 2.1.0.
        Assert.Equal("4.0.0", VersionFor<InheritedVersionFixtureActivity>(store));
        // The fixture assembly carries twelve concrete activities, including the typed result fixture,
        // the C# required-member fixture, and three input-options fixtures (abstract bases are excluded).
        Assert.Equal(12, store.Versions.Count);

        // Re-run: same content + same versions → zero new rows (SC-003 idempotency, DuplicateHandling.Skip).
        var idsBeforeRerun = store.Versions.Select(v => v.Id).OrderBy(id => id).ToArray();
        await reconciler.Reconcile(CancellationToken.None);
        Assert.Equal(12, store.Versions.Count);
        Assert.Equal(idsBeforeRerun, store.Versions.Select(v => v.Id).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task BumpedVersion_AppendsNewRow_RetainingTheOldOne()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var store = new InMemoryReconcilerHarness.CatalogStore();

        // First pass via the real CLR source persists all twelve concrete fixture activities.
        await InMemoryReconcilerHarness.BuildReconciler(store, FolderSource(folder.Path)).Reconcile(CancellationToken.None);
        Assert.Equal(12, store.Versions.Count);

        // Author bumps the versioned activity to 4.0.0 (new content + new version). The reconciler
        // matches the existing definition by ActivityTypeKey and appends the new version row.
        var bumped = StubSource("4.0.0");
        await InMemoryReconcilerHarness.BuildReconciler(store, bumped).Reconcile(CancellationToken.None);

        Assert.Equal(13, store.Versions.Count);
        var versioned = store.Definitions.Single(d => d.ActivityTypeKey == typeof(VersionedFixtureActivity).FullName);
        var versionsForVersioned = store.Versions.Where(v => v.DefinitionId == versioned.Id).Select(v => v.Version).OrderBy(v => v).ToList();
        Assert.Equal(new List<string> { "3.0.0", "4.0.0" }, versionsForVersioned);
    }

    [Fact]
    public async Task FolderSource_RebuildsCatalogWithTheSameActivityAndVersionIds()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var firstCatalog = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(firstCatalog, FolderSource(folder.Path)).Reconcile(CancellationToken.None);

        var firstIds = CatalogIds(firstCatalog);
        var versionedDefinitionId = firstCatalog.Definitions.Single(d => d.ActivityTypeKey == typeof(VersionedFixtureActivity).FullName).Id;
        var authoredVersionId = firstCatalog.Versions.Single(v => v.DefinitionId == versionedDefinitionId).Id;
        var rebuiltCatalog = new InMemoryReconcilerHarness.CatalogStore();

        await InMemoryReconcilerHarness.BuildReconciler(rebuiltCatalog, FolderSource(folder.Path)).Reconcile(CancellationToken.None);

        Assert.Equal(firstIds, CatalogIds(rebuiltCatalog));
        Assert.NotNull(rebuiltCatalog.Versions.SingleOrDefault(v => v.Id == authoredVersionId));
    }

    private static string VersionFor<TActivity>(InMemoryReconcilerHarness.CatalogStore store)
    {
        var definition = store.Definitions.Single(d => d.ActivityTypeKey == typeof(TActivity).FullName);
        return store.Versions.Single(v => v.DefinitionId == definition.Id).Version;
    }

    private static string[] CatalogIds(InMemoryReconcilerHarness.CatalogStore store) =>
        store.Versions
            .Select(v =>
            {
                var definition = store.Definitions.Single(d => d.Id == v.DefinitionId);
                return $"{definition.ActivityTypeKey}:{v.DefinitionId}:{v.Id}:{v.Version}";
            })
            .OrderBy(x => x)
            .ToArray();

    private static ClrActivityReconciliationSource FolderSource(string folderPath)
    {
        var options = Options.Create(new ClrReconciliationOptions { FolderPath = folderPath });
        var scanner = new ClrAssemblyScanner(new ActivityTypeVersionResolver(), new ActivityTypeCategoryResolver(), NullLogger<ClrAssemblyScanner>.Instance);
        return new ClrActivityReconciliationSource(scanner, options);
    }

    private static InMemoryReconcilerHarness.InMemorySource StubSource(string version) =>
        new InMemoryReconcilerHarness.InMemorySource(
            "stub",
            "CLR",
            new ActivityVersionReconciliationModel(
                Id: null,
                Version: version,
                ActivityTypeKey: typeof(VersionedFixtureActivity).FullName!,
                DisplayName: null,
                Category: null,
                Description: null,
                ProviderKey: "elsa.clr-activity",
                ProviderSchemaVersion: "1",
                ConsumerKey: "elsa.clr-activity",
                ConsumerSchemaVersion: "1",
                Descriptor: new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(VersionedFixtureActivity))),
                Inputs: [],
                Outputs: [],
                DesignFacets: []));

    private sealed class TempAssemblyFolder : IDisposable
    {
        public string Path { get; }

        private TempAssemblyFolder(string path) => Path = path;

        public static TempAssemblyFolder WithCopyOf(Assembly assembly)
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clr-recon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var source = assembly.Location;
            File.Copy(source, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(source)), overwrite: true);
            return new TempAssemblyFolder(dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
