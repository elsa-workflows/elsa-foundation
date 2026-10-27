using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using global::Nuplane.Abstractions;
using global::Nuplane.Abstractions.PackageStoreProtection;

namespace Elsa.Studio.Hosting.Nuplane;

/// <summary>Projects detached metadata bytes; every installed-file read uses the admitted content adapter.</summary>
internal static class PackageRegistryReader
{
    public static async Task<ModuleManagementPackage> ReadAsync(ActivePackage package, PackageContentReader reader,
        ICollection<ModuleManagementDiagnostic> diagnostics, CancellationToken token)
    {
        ModuleManagementPackageManifest? manifest = null;
        var dependencies = new List<ModuleManagementPackageDependency>();
        try
        {
            await using var scope = await reader.OpenAsync(package, token);
            foreach (var path in new[] { "elsa-package.json", "build/elsa-package.json" })
            {
                if (scope.Read(path, PackageContentReader.MaximumMetadataBytes, token) is not { } content) continue;
                var text = Text(content.Bytes);
                try
                {
                    var document = JsonNode.Parse(text);
                    var projected = document is JsonObject root && root["dependencies"] is JsonArray values
                        ? values.Select(Dependency).OfType<ModuleManagementPackageDependency>().ToArray() : [];
                    manifest ??= new("elsa-package", Path.Combine(package.InstallPath, path), document, null);
                    dependencies.AddRange(projected);
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
                {
                    manifest ??= new("elsa-package", Path.Combine(package.InstallPath, path), null, text);
                    diagnostics.Add(new(package.PackageId, "invalid-metadata", $"{path} is not valid JSON."));
                }
            }
            if (scope.FindRootFile(".nuspec", token) is { } nuspec && scope.Read(nuspec, PackageContentReader.MaximumMetadataBytes, token) is { } xml)
            {
                manifest ??= new("nuspec", Path.Combine(package.InstallPath, nuspec), null, Text(xml.Bytes));
                // NuSpec display/projection is not a second filesystem reader or dependency resolver.
                using var stream = new MemoryStream(xml.Bytes, writable: false);
                using var input = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = PackageContentReader.MaximumMetadataBytes
                });
                var document = XDocument.Load(input);
                dependencies.AddRange(document.Descendants().Where(element => element.Name.LocalName == "dependency")
                    .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("id")?.Value))
                    .Select(element => new ModuleManagementPackageDependency(element.Attribute("id")!.Value,
                        element.Attribute("version")?.Value ?? "",
                        element.Ancestors().FirstOrDefault(parent => parent.Name.LocalName == "group")?.Attribute("targetFramework")?.Value,
                        "nuspec")));
            }
        }
        catch (PackageStoreAdmissionException error) { Unavailable(error.Reason.ToString()); }
        catch (PackageContentSizeException error) { Unavailable(error.Message); }
        catch (PackageContentProfileException error) { Unavailable(error.Message); }
        catch (IOException) { Unavailable("Protected package metadata could not be read."); }
        catch (UnauthorizedAccessException) { Unavailable("Protected package metadata access was refused."); }
        catch (XmlException) { diagnostics.Add(new(package.PackageId, "invalid-metadata", "The NuSpec is not valid supported XML.")); }
        return new(package.PackageId, package.Version, package.FeedName ?? "", package.SourceName ?? "", package.InstallPath,
            manifest, dependencies.Distinct().OrderBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(dependency => dependency.VersionRange, StringComparer.OrdinalIgnoreCase).ToArray());

        void Unavailable(string reason)
        {
            // Do not fall back to an ordinary path reader if admission or the supported profile fails.
            manifest = new("manifest-error", package.InstallPath, null, reason);
            dependencies.Clear();
            diagnostics.Add(new(package.PackageId, "metadata-unavailable", reason));
        }
    }

    private static string Text(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var text = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return text.ReadToEnd();
    }

    private static ModuleManagementPackageDependency? Dependency(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var simple) && !string.IsNullOrWhiteSpace(simple))
            return new(simple, "", null, "elsa-package");
        if (node is not JsonObject item) return null;
        var id = Read(item, "id", "packageId", "name");
        return string.IsNullOrWhiteSpace(id) ? null : new(id, Read(item, "versionRange", "version", "range") ?? "",
            Read(item, "group", "targetFramework"), "elsa-package");
    }

    private static string? Read(JsonObject item, params string[] keys)
    {
        foreach (var key in keys)
            if (item[key] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        return null;
    }
}
