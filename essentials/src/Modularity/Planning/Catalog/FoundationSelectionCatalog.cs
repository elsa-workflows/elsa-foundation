using System.Text;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Catalog;

/// <summary>Loads the immutable Foundation-owned selection catalog shipped with the planner.</summary>
public static class FoundationSelectionCatalog
{
    private const string ResourcePrefix = "Elsa.Modularity.Planning.Catalogs.foundation-selection-catalog-v";
    private const string ClassicVersion = "4";
    private const string CurrentVersion = "5";

    /// <summary>Reads and strictly validates the current bundled catalog snapshot.</summary>
    public static SelectionCatalog Load() => LoadVersion(CurrentVersion);

    /// <summary>Uses a matching bundled snapshot, or the current snapshot so an unknown pin remains unresolved.</summary>
    public static SelectionCatalog LoadFor(CatalogPin pin)
    {
        foreach (var version in new[] { "1", "2", "3", ClassicVersion, CurrentVersion })
        {
            var catalog = LoadVersion(version);
            if (catalog.Id == pin.Id && catalog.Version == pin.Version && catalog.Digest == pin.Digest)
                return catalog;
        }

        return Load();
    }

    /// <summary>Uses the bundled classic snapshot when an exact classic profile is explicitly requested.</summary>
    public static SelectionCatalog LoadForProfile(string profilePin)
    {
        var classic = LoadVersion(ClassicVersion);
        return classic.Profiles.Any(profile => $"{profile.Id}@{profile.Version}" == profilePin)
            ? classic
            : Load();
    }

    private static SelectionCatalog LoadVersion(string version)
    {
        using var stream = typeof(FoundationSelectionCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + version + ".json")
            ?? throw new InvalidOperationException("The bundled Foundation selection catalog is missing.");
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        return SelectionJsonReader.ParseCatalog(reader.ReadToEnd());
    }
}
