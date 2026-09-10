using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

/// <summary>
/// Structural guard for the owning extension-point catalog. It checks that the catalog exists, carries the
/// three constitution §2.22.1 sections, and names every public seam type as a code-formatted identifier.
/// It deliberately asserts nothing about the catalog's prose so the document can be reworded freely.
/// </summary>
public sealed class DiagnosticsDocumentationTests
{
    private const string OwningCatalog = "src/Elsa/Diagnostics/Persistence/EXTENSION_POINTS.md";

    private static readonly string[] RequiredSections =
    [
        "Overridable contracts",
        "Implementable contributor interfaces",
        "Events"
    ];

    private static readonly string[] RequiredCodeIdentifiers =
    [
        "IDiagnosticsDrainTarget<TItem, TResult>",
        "IDiagnosticsPersistenceObserver",
        "IDiagnosticsPersistenceDrain",
        "DiagnosticsPersistenceObserverRegistrationValidator"
    ];

    [Fact]
    public void Owning_catalog_has_the_three_sections_and_names_every_public_seam()
    {
        var catalogPath = RepositoryPath(OwningCatalog);
        Assert.True(File.Exists(catalogPath), $"Missing owning catalog: {catalogPath}");

        var catalog = File.ReadAllText(catalogPath);
        var headings = Regex.Matches(catalog, @"^##\s+(?<title>.+?)\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups["title"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(RequiredSections, section => Assert.Contains(section, headings));
        Assert.All(RequiredCodeIdentifiers, identifier => Assert.Contains($"`{identifier}`", catalog, StringComparison.Ordinal));
    }

    [Fact]
    public void Root_extension_point_index_links_the_owning_catalog()
    {
        var rootIndex = File.ReadAllText(RepositoryPath("EXTENSION_POINTS.md"));

        Assert.Contains(OwningCatalog, rootIndex, StringComparison.Ordinal);
    }

    private static string RepositoryPath(string relativePath, [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../..", relativePath));
}
