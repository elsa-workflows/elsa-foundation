using System.Diagnostics;
using System.Text;

namespace Elsa.Maps.Generator;

/// <summary>
/// Repository-wide facts every map generator needs: where the root is, which files git considers part
/// of the tree, and the project graph read from <c>.csproj</c> files.
/// </summary>
/// <remarks>
/// File enumeration goes through <c>git ls-files</c> rather than a filesystem walk, matching the shell
/// generators this replaces: git respects <c>.gitignore</c> and reports the tracked casing, whereas a raw
/// walk picks up machine-local scratch files and on-disk casing, which churns the committed maps.
/// </remarks>
public sealed class RepoContext
{
    private RepoContext(string root) => Root = root;

    /// <summary>Absolute path to the repository root.</summary>
    public string Root { get; }

    /// <summary>
    /// Locates the repository root by walking up from the current directory looking for the solution file.
    /// </summary>
    public static RepoContext Discover(string? start = null)
    {
        var directory = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate the repository root (no Elsa.Server.slnx found).");

        return new RepoContext(directory.FullName);
    }

    /// <summary>
    /// Repo-relative, forward-slashed files matching the given git pathspecs, ordinally sorted.
    /// </summary>
    /// <remarks>
    /// Ordinal ordering is deliberate. The shell generators sort with the ambient locale, so their output
    /// depends on <c>LC_COLLATE</c> — byte order under <c>C.UTF-8</c>, something else elsewhere. Pinning
    /// ordinal here makes the maps reproducible across machines, which the shell version could not promise.
    /// </remarks>
    public IReadOnlyList<string> ListFiles(params string[] pathspecs)
    {
        var arguments = new List<string> { "-C", Root, "ls-files", "--cached", "--others", "--exclude-standard", "--" };
        arguments.AddRange(pathspecs);

        return RunGit(arguments)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim('\r'))
            .Where(line => line.Length > 0)
            .Where(line => File.Exists(Path.Combine(Root, line)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The absolute path for a repo-relative path.</summary>
    public string Absolute(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The current <c>HEAD</c> commit, or null outside a git checkout.</summary>
    public string? TryGetHead()
    {
        try
        {
            var head = RunGit(["-C", Root, "rev-parse", "HEAD"]).Trim();
            return head.Length == 0 ? null : head;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether any map input path has uncommitted changes, or null when git is unavailable.
    /// </summary>
    public bool? TryGetInputsDirty()
    {
        try
        {
            var status = RunGit([
                "-C", Root, "status", "--porcelain", "--",
                "src", "tests", "specs", "tools/maps", "Directory.Packages.props"
            ]);
            return status.Trim().Length > 0;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private string RunGit(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git; map generation requires a git checkout.");

        // Both pipes must be drained concurrently. Reading one to completion before the other deadlocks
        // as soon as git fills the second pipe's buffer -- `ls-files` over this repo emits thousands of
        // lines, and on a checkout with autocrlf enabled git also emits a stderr warning per file.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.Trim()}");

        return output;
    }
}
