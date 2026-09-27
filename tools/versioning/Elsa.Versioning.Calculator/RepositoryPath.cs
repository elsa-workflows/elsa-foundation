using System.Text;
using System.Text.RegularExpressions;

namespace Elsa.Versioning.Calculator;

/// <summary>Repository-relative path handling: forward slashes, no leading slash, directories with a trailing one.</summary>
internal static class RepositoryPath
{
    /// <summary>The directory a file sits in, with its trailing slash, or empty at the repository root.</summary>
    public static string DirectoryOf(string path) => path[..(path.LastIndexOf('/') + 1)];

    /// <summary>True for a project file, <c>.props</c> or <c>.targets</c>: a file MSBuild evaluates.</summary>
    public static bool IsMsBuildFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every directory from the repository root down to <paramref name="directory"/>, root first, each with its trailing
    /// slash (the root is empty).
    /// </summary>
    public static IEnumerable<string> SelfAndAncestors(string directory)
    {
        yield return string.Empty;
        for (var index = directory.IndexOf('/', StringComparison.Ordinal); index >= 0; index = directory.IndexOf('/', index + 1))
            yield return directory[..(index + 1)];
    }

    /// <summary>
    /// Resolves an MSBuild path as written into a repository-relative path, or null when it cannot be resolved
    /// statically: it uses another property, an item or metadata expansion or a property function, it is rooted, or
    /// it climbs out of the repository.
    /// </summary>
    /// <param name="written">The path as the MSBuild file spells it.</param>
    /// <param name="relativeTo">
    /// The directory a relative path is resolved against, with its trailing slash: the project's for an item, the
    /// importing file's for an import.
    /// </param>
    /// <param name="thisFileDirectory">What <c>$(MSBuildThisFileDirectory)</c> stands for, with its trailing slash.</param>
    /// <param name="projectDirectory">What <c>$(MSBuildProjectDirectory)</c> stands for, with a trailing slash.</param>
    public static string? Expand(string written, string relativeTo, string thisFileDirectory, string projectDirectory)
    {
        // A leading '/' marks the repository root while resolving. $(MSBuildThisFileDirectory) carries its trailing
        // slash and $(MSBuildProjectDirectory) does not, so the latter is only resolved where a separator follows it.
        var path = written.Replace('\\', '/')
            .Replace("$(MSBuildThisFileDirectory)", "/" + thisFileDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildProjectDirectory)/", "/" + projectDirectory, StringComparison.OrdinalIgnoreCase);
        if (path.Contains("$(", StringComparison.Ordinal) || path.Contains("@(", StringComparison.Ordinal) || path.Contains("%(", StringComparison.Ordinal) ||
            path.Contains('[', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal))
            return null;

        var segments = new List<string>();
        foreach (var segment in (path.StartsWith('/') ? path : "/" + relativeTo + path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0 || segments[^1].Contains('*', StringComparison.Ordinal))
                    return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// An MSBuild wildcard as a matcher over repository-relative paths: <c>*</c> and <c>?</c> stay within one path
    /// segment, and a <c>**</c> segment spans any number of directories. Case is ignored, which only ever widens a match.
    /// </summary>
    public static Regex Glob(string pattern)
    {
        var segments = pattern.Split('/');
        var regex = new StringBuilder("^");
        for (var index = 0; index < segments.Length; index++)
        {
            var last = index == segments.Length - 1;
            if (segments[index] == "**")
            {
                regex.Append(last ? ".*" : "(?:[^/]*/)*");
                continue;
            }

            regex.Append(Regex.Escape(segments[index]).Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal));
            if (!last)
                regex.Append('/');
        }

        return new Regex(regex.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
