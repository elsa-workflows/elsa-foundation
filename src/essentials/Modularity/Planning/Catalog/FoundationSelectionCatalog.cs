using System.Text;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Catalog;

/// <summary>Loads the immutable Foundation-owned selection catalog shipped with the planner.</summary>
public static class FoundationSelectionCatalog
{
    private const string ResourceName = "Elsa.Modularity.Planning.Catalogs.foundation-selection-catalog-v1.json";

    /// <summary>Reads and strictly validates the bundled catalog snapshot.</summary>
    public static SelectionCatalog Load()
    {
        using var stream = typeof(FoundationSelectionCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The bundled Foundation selection catalog is missing.");
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        return SelectionJsonReader.ParseCatalog(reader.ReadToEnd());
    }
}
