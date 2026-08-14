using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa3.Activities.Design.Import.Persistence.Groundwork;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Pins the Elsa 3 import manifest's physical surface to a committed golden file, rendered exactly
/// as <see cref="Elsa3ImportGroundworkStorageManifestSource"/> composes it in production. The golden
/// must stay byte-identical through the portable-surface migration and the Groundwork package bump.
/// </summary>
public class Elsa3ImportManifestPhysicalStorageGoldenTests
{
    [Fact]
    public void Manifest_physical_surface_matches_committed_golden() =>
        GroundworkManifestGoldenVerifier.Verify(
            LegacyGroundworkStorageManifestPhysicalizer.Physicalize(Elsa3ImportStorageManifest.Create()),
            "tests", "Elsa3", "Mapping", "Tests", "Goldens", "elsa3-import.json");
}
