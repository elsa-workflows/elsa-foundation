using System.IO.Compression;
using System.Text;
using Elsa.Versioning.Calculator;
using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests.Support;

/// <summary>
/// Stands in for <c>dotnet pack</c>: writes the packages a computation publishes, each carrying what a computed pack
/// puts in it — the version, the input fingerprint, the source commit and a range on each package it references.
/// </summary>
internal static class SyntheticPackages
{
    /// <summary>Packs every package the computation publishes from <paramref name="branch"/> into <paramref name="directory"/>, named as pack names them.</summary>
    public static void Pack(string directory, VersionComputation computation, string branch, SyntheticRepository repo)
    {
        Directory.CreateDirectory(directory);
        var label = PackProperties.LabelFor(computation, branch);
        foreach (var package in computation.Packages.Where(package => package.Affected))
        {
            var version = PackProperties.VersionOf(package, label);
            var dependencies = repo.Projects.Single(project => project.Path == package.Path).References
                .Select(reference => computation.Packages.SingleOrDefault(candidate => candidate.Path == reference))
                .OfType<ComputedPackage>()
                .Select(dependency => (dependency.PackageId, $"[{PackProperties.VersionOf(dependency, label)}, 5.0.0)"));
            File.WriteAllBytes(Path.Join(directory, $"{package.PackageId}.{version}.nupkg"),
                Bytes(package.PackageId, version, package.Fingerprint, computation.Commit, dependencies));
        }
    }

    /// <summary>
    /// A package's bytes. The commit is in them, as a real build embeds it in the assemblies, so the same inputs built at
    /// two commits give the same fingerprint and different bytes.
    /// </summary>
    public static byte[] Bytes(string id, string version, string? fingerprint, string commit, IEnumerable<(string Id, string Range)>? dependencies = null)
    {
        var entries = new Dictionary<string, string>
        {
            [$"{id}.nuspec"] = $"""
                               <?xml version="1.0" encoding="utf-8"?>
                               <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                                 <metadata>
                                   <id>{id}</id>
                                   <version>{version}</version>
                                   <repository type="git" commit="{commit}" />
                                   <dependencies>
                                     <group targetFramework="net10.0">
                               {string.Concat((dependencies ?? []).Select(dependency => $"        <dependency id=\"{dependency.Id}\" version=\"{dependency.Range}\" />\n"))}      </group>
                                   </dependencies>
                                 </metadata>
                               </package>
                               """,
            [$"lib/net10.0/{id}.dll"] = $"built at {commit}"
        };
        if (fingerprint is not null)
            entries[PackedPackage.FingerprintEntry] = $$"""{"schema_version":1,"fingerprint":"{{fingerprint}}"}""" + "\n";

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = archive.CreateEntry(name).Open();
                writer.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return stream.ToArray();
    }
}
