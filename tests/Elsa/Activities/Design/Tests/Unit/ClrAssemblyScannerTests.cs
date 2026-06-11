using System.Reflection;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
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
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(TypeInformation).Assembly);

        Assert.Empty(CreateScanner().Scan(folder.Path));
    }

    [Fact]
    public void UnreadableDll_IsLoggedAndSkipped_ScanStillCompletes()
    {
        using var folder = TempAssemblyFolder.WithCopyOf(typeof(UnannotatedFixtureActivity).Assembly);
        File.WriteAllText(Path.Combine(folder.Path, "garbage.dll"), "this is not a portable executable");

        // The junk DLL is skipped; the valid fixture assembly still yields its two activities.
        Assert.Equal(2, CreateScanner().Scan(folder.Path).Count);
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
