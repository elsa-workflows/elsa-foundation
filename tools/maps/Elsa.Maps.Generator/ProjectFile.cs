using System.Xml.Linq;

namespace Elsa.Maps.Generator;

/// <summary>
/// One parsed MSBuild file, read for its evaluation-time properties and items, plus the packable
/// precedence and version-line resolution that read those properties.
/// </summary>
public sealed record ProjectFile(string RelativePath, XDocument Document)
{
    /// <summary>SDKs whose props default <c>IsPackable</c> to false, as the Web and Worker SDKs do.</summary>
    private static readonly string[] NonPackableSdks = ["Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Worker"];

    public static ProjectFile Load(RepoContext repo, string relativePath) => new(relativePath, XDocument.Load(repo.Absolute(relativePath)));

    public string Name => Path.GetFileNameWithoutExtension(RelativePath);

    /// <summary><c>PackageId</c> defaults to <c>AssemblyName</c>, which defaults to the project name.</summary>
    public string PackageIdentity => Property("PackageId") ?? Property("AssemblyName") ?? Name;

    /// <summary>
    /// The last value a top-level <c>PropertyGroup</c> assigns, since MSBuild lets the last assignment win; null
    /// when none assigns a non-empty one.
    /// </summary>
    /// <remarks>
    /// A conditional or computed assignment fails generation instead of being read as a literal: this is a
    /// text scan, not an evaluation, and the dataset feeds publishing, so a guessed package id or packable
    /// state must be loud rather than quietly wrong.
    /// </remarks>
    public string? Property(string name)
    {
        var element = Document.Root!.Elements().Where(group => group.Name.LocalName == "PropertyGroup")
            .Elements().LastOrDefault(property => property.Name.LocalName == name);
        if (element is null) return null;

        if (element.Attribute("Condition") is not null || element.Parent!.Attribute("Condition") is not null || element.Value.Contains("$(", StringComparison.Ordinal))
            throw new InvalidOperationException($"{RelativePath}: <{name}> is conditional or computed, which the maps generator cannot evaluate.");

        var value = element.Value.Trim();
        return value.Length > 0 ? value : null;
    }

    /// <summary>Every item of the given type in a top-level <c>ItemGroup</c>, in document order.</summary>
    public IEnumerable<XElement> Items(string type) =>
        Document.Root!.Elements().Where(group => group.Name.LocalName == "ItemGroup")
            .Elements().Where(item => item.Name.LocalName == type);

    /// <summary>
    /// Whether packing produces a package, applying MSBuild's precedence: the project file, then the nearest
    /// <c>Directory.Build.props</c> (imported before the project body), then the SDK defaults — false for the
    /// Web and Worker SDKs and for test projects, true otherwise.
    /// </summary>
    /// <remarks>
    /// <c>tests/Directory.Build.props</c> is why a test project that declares nothing is not packable, and
    /// <c>Microsoft.NET.Test.Sdk</c> sets <c>IsTestProject</c> for the projects that reference it. Pack
    /// compares the value to <c>true</c> case-insensitively, so anything else counts as not packable.
    /// </remarks>
    public static bool IsPackable(RepoContext repo, ProjectFile file, Dictionary<string, XDocument?> inheritedProperties)
    {
        var declared = file.Property("IsPackable") ?? InheritedProperty(repo, file.RelativePath, "IsPackable", inheritedProperties);
        if (declared is not null)
            return string.Equals(declared, "true", StringComparison.OrdinalIgnoreCase);

        var isHost = (file.Document.Root!.Attribute("Sdk")?.Value ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries)
            .Any(sdk => NonPackableSdks.Contains(sdk.Split('/')[0], StringComparer.OrdinalIgnoreCase));
        var isTest = string.Equals(file.Property("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase)
                     || file.Items("PackageReference").Any(item => string.Equals(item.Attribute("Include")?.Value, "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));

        return !isHost && !isTest;
    }

    /// <summary>
    /// A property from the nearest <c>Directory.Build.props</c> above the project, which is the only one MSBuild
    /// imports automatically.
    /// </summary>
    private static string? InheritedProperty(RepoContext repo, string relativePath, string name, Dictionary<string, XDocument?> cache)
    {
        var directory = relativePath;
        do
        {
            var separator = directory.LastIndexOf('/');
            directory = separator < 0 ? string.Empty : directory[..separator];
            var props = directory.Length == 0 ? "Directory.Build.props" : $"{directory}/Directory.Build.props";

            if (!cache.TryGetValue(props, out var document))
                cache[props] = document = File.Exists(repo.Absolute(props)) ? XDocument.Load(repo.Absolute(props)) : null;

            if (document is not null)
                return new ProjectFile(props, document).Property(name);
        } while (directory.Length > 0);

        return null;
    }

    /// <summary>
    /// The raw <c>ElsaVersionLineAMembers</c> text from <c>VersionLines.props</c>, the one reviewed Line A list
    /// (ADR 0067) that <c>Directory.Build.props</c> also reads.
    /// </summary>
    public static string ReadLineAMembers(RepoContext repo)
    {
        const string path = "VersionLines.props";
        if (!File.Exists(repo.Absolute(path)))
            throw new InvalidOperationException($"{path} is missing; the dependency map reads Line A membership from it.");

        return new ProjectFile(path, XDocument.Load(repo.Absolute(path))).Property("ElsaVersionLineAMembers")
               ?? throw new InvalidOperationException($"{path} declares no ElsaVersionLineAMembers.");
    }

    /// <summary>
    /// A project's version line, with the whole-name semantics <c>Directory.Build.props</c> gives it: MSBuild tests
    /// <c>$(ElsaVersionLineAMembers.Contains(';$(MSBuildProjectName);'))</c>, so <c>Elsa.Tasks</c> is Line B even
    /// though <c>Elsa.Tasks.Core</c> is Line A.
    /// </summary>
    public static string LineOf(string projectName, string lineAMembers) =>
        lineAMembers.Contains($";{projectName};", StringComparison.Ordinal) ? "A" : "B";
}
