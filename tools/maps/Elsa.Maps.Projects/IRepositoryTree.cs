using System.Text;
using System.Xml.Linq;

namespace Elsa.Maps.Projects;

/// <summary>
/// The files the project-graph scan reads: a working tree for the maps generator, or one commit's git objects for the
/// versioning calculator (spec 259). Paths are repository-relative and forward-slashed.
/// </summary>
/// <remarks>
/// The scan reads nothing else: no restore output, no environment, no clock. So the same tree gives the same project
/// graph whichever view reads it, which is what lets the calculator derive the dependency-map dataset from a commit
/// instead of reading a committed copy.
/// </remarks>
public interface IRepositoryTree
{
    /// <summary>Every backend project file the maps describe (<see cref="RepoLayout.IsProjectPath"/>), in ordinal order.</summary>
    IReadOnlyList<string> ProjectFiles();

    /// <summary>True when the tree has a file at <paramref name="path"/>.</summary>
    bool Exists(string path);

    /// <summary>The file's bytes; fails when the tree has no such file.</summary>
    byte[] Read(string path);
}

/// <summary>Parsing helpers over <see cref="IRepositoryTree.Read"/>, decoding as the file APIs they replace did.</summary>
public static class RepositoryTreeReading
{
    /// <summary>The file as text, with a byte-order mark detected and dropped as <c>File.ReadAllText</c> does.</summary>
    public static string ReadText(this IRepositoryTree tree, string path)
    {
        using var reader = new StreamReader(new MemoryStream(tree.Read(path)), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>The file parsed as XML, as <c>XDocument.Load(path)</c> parses it.</summary>
    public static XDocument ReadXml(this IRepositoryTree tree, string path)
    {
        using var stream = new MemoryStream(tree.Read(path));
        return XDocument.Load(stream);
    }

    /// <summary>
    /// <paramref name="include"/> resolved against <paramref name="directory"/> (repository-relative, forward-slashed,
    /// no trailing slash; empty at the root), as MSBuild resolves a relative item path; null when it is rooted or leaves
    /// the repository.
    /// </summary>
    /// <remarks>
    /// Pure path arithmetic, so a git tree, which has no file system to ask, resolves exactly as a checkout does.
    /// </remarks>
    public static string? Resolve(string directory, string include)
    {
        var relative = include.Replace('\\', '/');
        if (relative.StartsWith('/') || Path.IsPathRooted(include) || (relative.Length > 1 && relative[1] == ':'))
            return null;

        var segments = new List<string>(directory.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null;
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
