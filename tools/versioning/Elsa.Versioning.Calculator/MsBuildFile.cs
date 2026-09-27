using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// What the calculator reads out of one MSBuild file, statically: what it imports, which files it names, whether it
/// packs a tool, and a digest of its content that ignores comments and layout.
/// </summary>
/// <remarks>
/// This is not an evaluation. Anything that cannot be read literally is treated in the direction that marks more
/// packages changed, never fewer: an import whose path cannot be resolved fails the computation, and an item pattern
/// built from properties is taken to name every file.
/// </remarks>
internal sealed class MsBuildFile
{
    /// <summary>
    /// The properties that carry each line's <c>major.minor</c> and Line A membership (<c>VersionLines.props</c>). They
    /// reach a package through the version-line rules (spec 150 FR-002, FR-010) rather than as content, so a change to
    /// one of them does not by itself mark every package changed.
    /// </summary>
    public static readonly IReadOnlySet<string> VersionLineProperties =
        new HashSet<string>(["ElsaVersion", "ElsaContractsVersion", "ElsaVersionLineAMembers"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Item types whose includes are package ids or names, never files in the package.</summary>
    private static readonly IReadOnlySet<string> NonFileItemTypes = new HashSet<string>(
        ["PackageReference", "PackageVersion", "GlobalPackageReference", "PackageDownload", "ProjectReference", "FrameworkReference",
         "Using", "InternalsVisibleTo", "AssemblyAttribute", "Folder"],
        StringComparer.OrdinalIgnoreCase);

    private MsBuildFile(XElement? root, byte[] content)
    {
        if (root is null)
        {
            // Unparseable: MSBuild would fail on it too. Its raw bytes still count, and it is taken to name every file.
            ContentDigest = "raw:" + Sha256(content);
            NamesEveryFile = true;
            return;
        }

        var elements = root.DescendantsAndSelf().ToArray();
        Imports = elements
            .Where(element => element.Name.LocalName == "Import" && element.Attribute("Sdk") is null)
            .Select(element => element.Attribute("Project")?.Value ?? string.Empty)
            .ToArray();
        ItemPatterns = elements
            .Where(element => element.Parent?.Name.LocalName == "ItemGroup" && !NonFileItemTypes.Contains(element.Name.LocalName))
            .SelectMany(element => new[] { element.Attribute("Include")?.Value, element.Attribute("Update")?.Value })
            .OfType<string>()
            .SelectMany(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var properties = elements.Where(element => element.Parent?.Name.LocalName == "PropertyGroup").ToArray();
        PropertyValues = properties
            .SelectMany(property => property.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        SetProperties = properties.Select(property => property.Name.LocalName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        PacksAsTool = properties.Any(property =>
            string.Equals(property.Name.LocalName, "PackAsTool", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(property.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase) || property.Value.Contains("$(", StringComparison.Ordinal)));

        ContentDigest = CanonicalDigest(root, element => element.Parent?.Name.LocalName == "PropertyGroup" && VersionLineProperties.Contains(element.Name.LocalName));
    }

    /// <summary>
    /// A digest of the file with comments, insignificant whitespace and <see cref="VersionLineProperties"/> removed, so
    /// an edit that cannot change a build does not register as one.
    /// </summary>
    public string ContentDigest { get; }

    /// <summary>The <c>Project</c> of every non-SDK import, as written.</summary>
    public IReadOnlyList<string> Imports { get; } = [];

    /// <summary>Every <c>Include</c> and <c>Update</c> entry of every item that can name a file, as written.</summary>
    public IReadOnlyList<string> ItemPatterns { get; } = [];

    /// <summary>Every property value, split on <c>;</c>.</summary>
    public IReadOnlyList<string> PropertyValues { get; } = [];

    /// <summary>The names of every property the file assigns.</summary>
    public IReadOnlyList<string> SetProperties { get; } = [];

    /// <summary>True when the file sets <c>PackAsTool</c>, or sets it to something computed.</summary>
    public bool PacksAsTool { get; }

    /// <summary>True when the file could not be read, so which files it names is unknown.</summary>
    public bool NamesEveryFile { get; }

    public static MsBuildFile Parse(byte[] content)
    {
        try
        {
            return new MsBuildFile(LoadRoot(content, "build file"), content);
        }
        catch (InvalidOperationException)
        {
            return new MsBuildFile(null, content);
        }
    }

    /// <summary>The root element of an XML document, without insignificant whitespace; fails naming <paramref name="source"/>.</summary>
    internal static XElement LoadRoot(byte[] content, string source)
    {
        try
        {
            using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return XDocument.Load(reader, LoadOptions.None).Root ?? throw new InvalidOperationException($"{source} has no root element.");
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException($"{source} is not well-formed XML: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// True when this file names <paramref name="file"/> for a project in <paramref name="projectDirectory"/>: an item
    /// include or update matching it, or a property value spelling its name or its path within the project.
    /// </summary>
    /// <param name="definedIn">The repository-relative path of this file, for <c>$(MSBuildThisFileDirectory)</c>.</param>
    public bool Names(string file, string projectDirectory, string definedIn)
    {
        if (NamesEveryFile)
            return true;

        var inProject = file.StartsWith(projectDirectory, StringComparison.Ordinal) ? file[projectDirectory.Length..] : null;
        var name = file[(file.LastIndexOf('/') + 1)..];
        if (PropertyValues.Select(value => value.Replace('\\', '/')).Any(value =>
                string.Equals(value, name, StringComparison.OrdinalIgnoreCase) || string.Equals(value, inProject, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Items resolve against the project's directory, wherever they are declared.
        return ItemPatterns.Any(pattern =>
            RepositoryPath.Expand(pattern, projectDirectory, RepositoryPath.DirectoryOf(definedIn), projectDirectory) is not { } resolved ||
            RepositoryPath.Glob(resolved).IsMatch(file));
    }

    internal static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>
    /// A digest of an MSBuild document with comments, insignificant whitespace and the <paramref name="excluded"/>
    /// elements removed, along with any property or item group their removal leaves empty. None of that can change
    /// what MSBuild evaluates.
    /// </summary>
    internal static string CanonicalDigest(XElement root, Func<XElement, bool> excluded)
    {
        var canonical = new XElement(root);
        canonical.DescendantNodesAndSelf().OfType<XComment>().ToList().ForEach(comment => comment.Remove());
        canonical.Descendants().Where(excluded).ToList().ForEach(element => element.Remove());
        canonical.Descendants().Where(element => element.Name.LocalName is "PropertyGroup" or "ItemGroup" && !element.HasElements).ToList()
            .ForEach(element => element.Remove());
        return "xml:" + Sha256(Encoding.UTF8.GetBytes(canonical.ToString(SaveOptions.DisableFormatting)));
    }
}
