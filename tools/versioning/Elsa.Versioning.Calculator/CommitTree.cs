using Elsa.Maps.Projects;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// One commit's tree as the project-graph scan reads it: the files are the commit's own git objects, never the working
/// tree, so a dataset derived through it is a function of the commit alone (spec 150 FR-009, spec 259 FR-001).
/// </summary>
internal sealed class CommitTree(ObjectCache objects, string commit, IReadOnlyDictionary<string, TreeEntry> tree) : IRepositoryTree
{
    /// <inheritdoc />
    public IReadOnlyList<string> ProjectFiles() => tree.Keys.Where(RepoLayout.IsProjectPath).Order(StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public bool Exists(string path) => tree.ContainsKey(path);

    /// <inheritdoc />
    public byte[] Read(string path) =>
        tree.TryGetValue(path, out var entry)
            ? objects.Read(entry.ObjectId)
            : throw new InvalidOperationException($"Commit {commit} has no {path}.");
}
