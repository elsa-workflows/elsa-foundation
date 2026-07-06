using System.Reflection;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Tests.ClrFixture;
using Elsa.Primitives.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Resilient-scan tests for <see cref="ClrAssemblyScanner"/> (FR-023, §2.23.2). Each case lays a
/// real folder of DLLs and asserts the scanner discovers activity-bearing assemblies, skips the
/// rest silently, and never aborts on an unreadable file.
/// </summary>
public sealed class ClrAssemblyScannerTests
{
    private static ClrAssemblyScanner CreateScanner() =>
        new(new ActivityTypeVersionResolver(), new ActivityTypeCategoryResolver(), NullLogger<ClrAssemblyScanner>.Instance);

    [Fact]
    public void ActivityAssembly_IsDiscovered_WithResolvedVersions()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var models = CreateScanner().Scan(folder.Path);

        var byKey = models.ToDictionary(m => m.ActivityTypeKey, m => m.Version);
        Assert.Equal("2.1.0", byKey[typeof(UnannotatedFixtureActivity).FullName!]);
        Assert.Equal("3.0.0", byKey[typeof(VersionedFixtureActivity).FullName!]);
    }

    [Fact]
    public void SelfDeclaredRequiredInput_MapsIsRequired()
    {
        // Guards the pre-existing (previously unasserted) self-declared [Required] → IsRequired mapping
        // so the base-chain refactor (issue #417 item 3) provably preserves it.
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var input = InputFor<UnannotatedFixtureActivity>(CreateScanner().Scan(folder.Path), nameof(UnannotatedFixtureActivity.Message));

        Assert.True(input.IsRequired);
    }

    [Fact]
    public void InheritedRequiredInput_MapsIsRequired()
    {
        // The derived activity re-declares its input with `new` and no [Required]; the attribute lives
        // only on the base declaration. The reflection-only scanner reads the attribute-less derived
        // declaration first, so it must walk the base-property chain to find [Required] (issue #417 item 3).
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var inputs = InputsFor<InheritsRequiredFixtureActivity>(CreateScanner().Scan(folder.Path), nameof(RequiredInputBaseActivity.InheritedRequired));

        // Whatever the scanner surfaces for the hidden/new pair, the inherited [Required] must win.
        Assert.All(inputs, input => Assert.True(input.IsRequired));
        Assert.NotEmpty(inputs);
    }

    private static InputDefinition InputFor<TActivity>(IReadOnlyList<ActivityVersionReconciliationModel> models, string inputName) =>
        models.Single(m => m.ActivityTypeKey == typeof(TActivity).FullName).Inputs.Single(i => i.Name == inputName);

    private static IReadOnlyList<InputDefinition> InputsFor<TActivity>(IReadOnlyList<ActivityVersionReconciliationModel> models, string inputName) =>
        models.Single(m => m.ActivityTypeKey == typeof(TActivity).FullName).Inputs.Where(i => i.Name == inputName).ToList();

    [Fact]
    public void ApplicationOutputFolder_DiscoversPrimitiveActivities()
    {
        var models = CreateScanner().Scan(AppContext.BaseDirectory);

        Assert.Contains(models, m => m.ActivityTypeKey == typeof(WriteLine).FullName);
    }

    [Fact]
    public void DiscoveredActivities_AreCategorised_ByAssemblyNameLastSegment()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);

        var models = CreateScanner().Scan(folder.Path);

        // The fixture assembly is "Elsa.Activities.Design.Tests.ClrFixture" → category "ClrFixture".
        Assert.All(models, m => Assert.Equal("ClrFixture", m.Category));
    }

    [Fact]
    public void NonActivityAssembly_IsSilentlySkipped()
    {
        // Elsa.Primitives carries no IActivity implementations.
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(ClrActivityDescriptor).Assembly);

        Assert.Empty(CreateScanner().Scan(folder.Path));
    }

    [Fact]
    public void UnreadableDll_IsLoggedAndSkipped_ScanStillCompletes()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        File.WriteAllText(Path.Combine(folder.Path, "garbage.dll"), "this is not a portable executable");

        // The junk DLL is skipped; the valid fixture assembly still yields its five concrete activities.
        Assert.Equal(5, CreateScanner().Scan(folder.Path).Count);
    }

    [Fact]
    public void RepeatedScans_YieldIdenticalResults_WithCachedFrameworkPaths()
    {
        // The framework resolver paths (base dir, TPA, runtime dir) are cached across Scan() calls
        // (issue #417 item 2). A second scan must resolve exactly the same activities and versions —
        // proving the cached overlay is re-applied intact and per-call state is not corrupted.
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var scanner = CreateScanner();

        var first = scanner.Scan(folder.Path).ToDictionary(m => m.ActivityTypeKey, m => m.Version);
        var second = scanner.Scan(folder.Path).ToDictionary(m => m.ActivityTypeKey, m => m.Version);

        Assert.Equal(first, second);
        Assert.Equal("2.1.0", second[typeof(UnannotatedFixtureActivity).FullName!]);
        Assert.Equal("3.0.0", second[typeof(VersionedFixtureActivity).FullName!]);
    }

    [Fact]
    public void FolderAssembly_IsResolvable_EvenAfterCachedFrameworkPathsAreWarm()
    {
        // Warm the framework-path cache with a base-directory scan first, then scan a temp folder.
        // The folder DLLs must still be added ahead of the cached framework closure so the author's
        // assembly resolves (folder-precedence overlay preserved) — a broken overlay would drop these
        // activities.
        var scanner = CreateScanner();
        _ = scanner.Scan(AppContext.BaseDirectory);

        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        var models = scanner.Scan(folder.Path);

        Assert.Contains(models, m => m.ActivityTypeKey == typeof(UnannotatedFixtureActivity).FullName);
    }

    [Fact]
    public void EmptyFolder_YieldsNoModels()
    {
        using var folder = TempAssemblyFolder.Empty();

        Assert.Empty(CreateScanner().Scan(folder.Path));
    }

    [Fact]
    public void NonexistentFolder_YieldsNoModels()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Empty(CreateScanner().Scan(missing));
    }

    private sealed class TempAssemblyFolder : IDisposable
    {
        public string Path { get; }

        private TempAssemblyFolder(string path) => Path = path;

        public static TempAssemblyFolder Empty()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clr-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return new TempAssemblyFolder(dir);
        }

        public static TempAssemblyFolder WithCopyOf(Assembly assembly)
        {
            var folder = Empty();
            var source = assembly.Location;
            File.Copy(source, System.IO.Path.Combine(folder.Path, System.IO.Path.GetFileName(source)), overwrite: true);
            return folder;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a locked file must not fail the test.
            }
        }
    }
}
