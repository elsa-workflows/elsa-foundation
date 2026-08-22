using System.Text;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Byte-significant checked-in artifacts must reach the worktree with LF endings on every platform.
/// </summary>
/// <remarks>
/// <para>
/// Several suites hash these files byte-for-byte and compare against a constant recorded beside the test. Git
/// stores them as LF; a Windows checkout with <c>core.autocrlf=true</c> and no governing attribute produces CRLF,
/// so the bytes hashed depend on the machine while the expected hash does not. The failure that produces says
/// nothing about line endings — it is a bare hash mismatch — and it points at no real defect.
/// </para>
/// <para>
/// That cost real review time on PR #1330. An architect measured the architecture suite green where a Windows
/// checkout measured it red, and the entire difference was this. Three checkpoint-fence attachment hashes were
/// reproduced exactly by normalizing to LF, which is what identified the cause.
/// </para>
/// <para>
/// <c>.gitattributes</c> is the fix; this test is the guard on the fix. A newly added hashed artifact that the
/// attribute file does not cover shows up here as a named failure, rather than later as an unexplained hash
/// mismatch on someone's laptop.
/// </para>
/// </remarks>
public sealed class HashedArtifactLineEndingGuardTests
{
    /// <summary>Directory segments whose contents are hashed or compared byte-for-byte.</summary>
    private static readonly string[] ByteSignificantSegments =
    [
        "Baselines",
        "Goldens",
        "Fixtures",
        "ledger-attachments"
    ];

    /// <summary>Extensions that are genuinely binary and must never be line-ending converted.</summary>
    private static readonly string[] BinaryExtensions =
    [
        ".gz", ".zip", ".nupkg", ".png", ".jpg", ".jpeg", ".pdf", ".dll", ".db", ".bin"
    ];

    [Fact]
    public void No_byte_significant_artifact_carries_crlf_in_the_worktree()
    {
        var root = RepoRoot();
        var candidates = ByteSignificantSegments
            .SelectMany(segment => Directory.EnumerateDirectories(
                Path.Combine(root, "tests"), segment, SearchOption.AllDirectories))
            .Concat(Directory.EnumerateDirectories(
                Path.Combine(root, "specs"), "ledger-attachments", SearchOption.AllDirectories))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !BinaryExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        // A traversal that found nothing would satisfy the emptiness check below without proving anything.
        Assert.True(
            candidates.Length > 100,
            $"The traversal reached only {candidates.Length} byte-significant files; it is not walking the tree.");

        var offenders = candidates
            .Where(ContainsCrLf)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These byte-significant files carry CRLF, so any hash taken over them differs from CI. Add a "
            + "`text eol=lf` rule to .gitattributes covering them, then re-checkout or renormalize:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Take(25)));
    }

    private static bool ContainsCrLf(string path)
    {
        using var stream = File.OpenRead(path);
        var previous = -1;
        int current;
        while ((current = stream.ReadByte()) != -1)
        {
            if (previous == '\r' && current == '\n')
                return true;
            previous = current;
        }

        return false;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitattributes")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
