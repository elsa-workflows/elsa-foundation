using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Elsa.Versioning.Calculator;

/// <summary>One entry of a commit's tree: a tracked file (or submodule) with its mode and object id.</summary>
public sealed record TreeEntry(string Path, string Mode, string ObjectId);

/// <summary>
/// Read-only access to a local git repository: the only way the calculator sees history.
/// </summary>
/// <remarks>
/// Every read goes to git's object database, never to the working tree, so a result depends only on the commits
/// and objects named and not on what is checked out, modified or untracked (spec 150 FR-009). The commands are
/// limited to plumbing that reads local objects, so a computation can never fetch, write, or contact a remote,
/// let alone a package feed (FR-006, FR-013, SC-008).
/// </remarks>
public sealed partial class GitRepository
{
    /// <summary>The only git commands the calculator runs. None of them touches the network or writes.</summary>
    internal static readonly IReadOnlySet<string> ReadOnlyCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "cat-file", "ls-tree", "merge-base", "rev-parse"
    };

    public GitRepository(string directory) => Directory = Path.GetFullPath(directory);

    /// <summary>The directory git is run in; any directory inside the repository will do.</summary>
    public string Directory { get; }

    /// <summary>True when <paramref name="text"/> is a full object id, as the record stores commits.</summary>
    public static bool IsFullObjectId(string? text) => text is not null && ObjectIdPattern().IsMatch(text);

    /// <summary>The full commit id a revision names; fails when it names no commit.</summary>
    public string ResolveCommit(string revision)
    {
        var result = Run("rev-parse", "--verify", "--quiet", "--end-of-options", $"{revision}^{{commit}}");
        var commit = Encoding.UTF8.GetString(result.Output).Trim();
        if (result.ExitCode != 0 || !IsFullObjectId(commit))
            throw new InvalidOperationException($"'{revision}' does not name a commit in {Directory}.");

        return commit;
    }

    /// <summary>True when the commit's object is present locally. A shallow clone lacks older ones.</summary>
    public bool HasCommit(string commit) => Run("cat-file", "-e", $"{commit}^{{commit}}").ExitCode == 0;

    /// <summary>True when <paramref name="ancestor"/> is <paramref name="descendant"/> or one of its ancestors.</summary>
    public bool IsAncestorOrSelf(string ancestor, string descendant)
    {
        var result = Run("merge-base", "--is-ancestor", ancestor, descendant);
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new InvalidOperationException($"git merge-base --is-ancestor {ancestor} {descendant} failed: {result.Error}")
        };
    }

    /// <summary>Every file and submodule in the commit's tree, with repository-relative paths.</summary>
    public IReadOnlyList<TreeEntry> ListTree(string commit)
    {
        var output = Encoding.UTF8.GetString(Require(Run("ls-tree", "-r", "-z", "--full-tree", commit), $"ls-tree {commit}"));
        var entries = new List<TreeEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<mode> SP <type> SP <object> TAB <path>"
            var tab = record.IndexOf('\t', StringComparison.Ordinal);
            var fields = record[..tab].Split(' ');
            entries.Add(new TreeEntry(record[(tab + 1)..], fields[0], fields[2]));
        }

        return entries;
    }

    /// <summary>The bytes of a blob, named by object id or by <c>&lt;revision&gt;:&lt;path&gt;</c>.</summary>
    public byte[] ReadBlob(string objectName) =>
        TryReadBlob(objectName) ?? throw new InvalidOperationException($"{objectName} names no file in {Directory}.");

    /// <summary>The bytes of a blob, or null when the name resolves to none.</summary>
    public byte[]? TryReadBlob(string objectName)
    {
        if (objectName.StartsWith('-'))
            throw new ArgumentException($"'{objectName}' is not an object name.", nameof(objectName));

        var result = Run("cat-file", "blob", objectName);
        return result.ExitCode == 0 ? result.Output : null;
    }

    private static byte[] Require(GitResult result, string what) =>
        result.ExitCode == 0 ? result.Output : throw new InvalidOperationException($"git {what} failed: {result.Error}");

    private GitResult Run(params string[] arguments)
    {
        if (!ReadOnlyCommands.Contains(arguments[0]))
            throw new InvalidOperationException($"git {arguments[0]} is not a read-only command the calculator may run.");

        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in (string[])["-C", Directory, .. arguments])
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Failed to start git; the calculator reads history through it.");

        // Drain both pipes at once: reading one to the end first deadlocks when git fills the other's buffer.
        using var output = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        outputTask.GetAwaiter().GetResult();

        return new GitResult(process.ExitCode, output.ToArray(), errorTask.GetAwaiter().GetResult().Trim());
    }

    private sealed record GitResult(int ExitCode, byte[] Output, string Error);

    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectIdPattern();
}
