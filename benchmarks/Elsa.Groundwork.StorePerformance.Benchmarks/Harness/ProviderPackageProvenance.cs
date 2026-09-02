using System.Xml.Linq;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>Owns the exact provider-package identity retained by every #646 request and artifact.</summary>
public static class ProviderPackageProvenance
{
    private static readonly string[] EfAdapters = ["ef-secret-repository", "ef-diagnostics-oracle"];

    public static IReadOnlyList<string> RequiredPackageNames(string adapter, string provider) =>
        EfAdapters.Contains(adapter, StringComparer.Ordinal)
            ? ["Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Sqlite"]
            : [provider switch
            {
                "sqlite" => "Groundwork.Sqlite",
                "postgresql" => "Groundwork.PostgreSql",
                "sqlserver" => "Groundwork.SqlServer",
                "mongodb" => "Groundwork.MongoDb",
                _ => throw new PerformanceContractException(
                    $"No provider package provenance is registered for '{provider}'.")
            }];

    public static IReadOnlyDictionary<string, string> CurrentVersions(
        string repositoryRoot,
        string adapter,
        string provider)
    {
        var declared = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"))
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PackageVersion", StringComparison.Ordinal))
            .Select(element => new
            {
                Name = (string?)element.Attribute("Include") ?? "",
                Version = (string?)element.Attribute("Version") ?? ""
            })
            .Where(item => item.Name.Length > 0)
            .ToDictionary(item => item.Name, item => item.Version, StringComparer.Ordinal);

        var required = RequiredPackageNames(adapter, provider);
        var missing = required.Where(name => !declared.TryGetValue(name, out var version) || string.IsNullOrWhiteSpace(version)).ToArray();
        if (missing.Length > 0)
            throw new PerformanceContractException(
                $"Directory.Packages.props does not declare provider package provenance: {string.Join(", ", missing)}.");
        return required.ToDictionary(name => name, name => declared[name], StringComparer.Ordinal);
    }

    public static void RequireExactCurrent(
        string repositoryRoot,
        string adapter,
        string provider,
        IReadOnlyDictionary<string, string> supplied)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        var expected = CurrentVersions(repositoryRoot, adapter, provider);
        if (supplied.Count != expected.Count || expected.Any(pair =>
                !supplied.TryGetValue(pair.Key, out var version) ||
                !string.Equals(version, pair.Value, StringComparison.Ordinal)))
            throw new PerformanceContractException(
                $"Provider package provenance for '{adapter}/{provider}' must exactly match current central package declarations.");
    }
}
