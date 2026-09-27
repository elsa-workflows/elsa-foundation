using System.Xml.Linq;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// The root <c>Directory.Packages.props</c> at one commit, split the way spec 150 FR-003 and FR-004 need it.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>PackageVersion</c> entry is read per package id and belongs only to the projects with an external edge to
/// that id (FR-003), so a bump advances exactly those projects (SC-004). An entry no project references directly — a
/// transitive pin — therefore advances nothing, as FR-003 is written.
/// </para>
/// <para>
/// Everything else in the file — its properties and any other items — is a repository-wide input (FR-004): it can
/// change every project's restore and nuspec, and there is no edge to be precise with.
/// </para>
/// <para>Comments and layout take no part, so a comment-only edit advances nothing (US3 scenario 3).</para>
/// </remarks>
internal sealed class CentralPackages(IReadOnlyDictionary<string, string> entries, string remainder)
{
    public const string RelativePath = "Directory.Packages.props";

    /// <summary>A digest of everything in the file except its <c>PackageVersion</c> entries.</summary>
    public string Remainder { get; } = remainder;

    /// <param name="content">The file's bytes, or null when the commit has none.</param>
    public static CentralPackages Parse(byte[]? content)
    {
        if (content is null)
            return new CentralPackages(new Dictionary<string, string>(), "absent");

        static bool IsPackageVersion(XElement element) => element.Name.LocalName == "PackageVersion";

        var root = MsBuildFile.LoadRoot(content, RelativePath);
        root.DescendantNodes().OfType<XComment>().ToList().ForEach(comment => comment.Remove());
        var byId = root.Descendants().Where(IsPackageVersion)
            .GroupBy(element => element.Attribute("Include")?.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => string.Join(' ', group.Select(element => element.ToString(SaveOptions.DisableFormatting))),
                StringComparer.OrdinalIgnoreCase);

        return new CentralPackages(byId, MsBuildFile.CanonicalDigest(root, IsPackageVersion));
    }

    /// <summary>The entries for one package id, as written; empty when the file has none for it.</summary>
    public string EntriesFor(string packageId) => entries.GetValueOrDefault(packageId) ?? string.Empty;
}
