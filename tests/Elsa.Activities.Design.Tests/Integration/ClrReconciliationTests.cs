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
/// (with the real scanner + version resolver), the real <see cref="ActivityVersionsReconcilingHandler"/>,
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
        Assert.Equal(2, store.Versions.Count);

        // Re-run: same content + same versions → zero new rows (SC-003 idempotency, DuplicateHandling.Skip).
        await reconciler.Reconcile(CancellationToken.None);
        Assert.Equal(2, store.Versions.Count);
    }

    [Fact]
    public async Task BumpedVersion_AppendsNewRow_RetainingTheOldOne()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var store = new InMemoryReconcilerHarness.CatalogStore();

        // First pass via the real CLR source persists 2.1.0 + 3.0.0.
        await InMemoryReconcilerHarness.BuildReconciler(store, FolderSource(folder.Path)).Reconcile(CancellationToken.None);
        Assert.Equal(2, store.Versions.Count);

        // Author bumps the versioned activity to 4.0.0 (new content + new version). The reconciler
        // matches the existing definition by ActivityTypeKey and appends the new version row.
        var bumped = StubSource("4.0.0");
        await InMemoryReconcilerHarness.BuildReconciler(store, bumped).Reconcile(CancellationToken.None);

        Assert.Equal(3, store.Versions.Count);
        var versioned = store.Definitions.Single(d => d.ActivityTypeKey == typeof(VersionedFixtureActivity).FullName);
        var versionsForVersioned = store.Versions.Where(v => v.DefinitionId == versioned.Id).Select(v => v.Version).OrderBy(v => v).ToList();
        Assert.Equal(new List<string> { "3.0.0", "4.0.0" }, versionsForVersioned);
    }

    private static string VersionFor<TActivity>(InMemoryReconcilerHarness.CatalogStore store)
    {
        var definition = store.Definitions.Single(d => d.ActivityTypeKey == typeof(TActivity).FullName);
        return store.Versions.Single(v => v.DefinitionId == definition.Id).Version;
    }

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
                ImplementationKind: ClrImplementationDescriptor.KindValue,
                ImplementationDescriptor: new ClrImplementationDescriptor(TypeInformation.FromType(typeof(VersionedFixtureActivity))),
                Inputs: [],
                Outputs: [],
                Ports: []));

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
