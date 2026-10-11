using System.Security.Cryptography;
using System.Text;
using Elsa.Versioning.Calculator;
using Elsa.Versioning.Calculator.Tests.Support;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// The fixture's repositories start no background work (#2365). By default git detaches a <c>maintenance run --auto</c>
/// from every commit, fetch and receive-pack, and one still repacking while <see cref="SyntheticRepository.Dispose"/>
/// deletes the directory fails the delete with "Directory not empty". Both repositories are made eligible for a repack
/// and told to keep maintenance in the foreground, so a maintenance run that did start would leave a pack behind before
/// the command returned.
/// </summary>
public sealed class SyntheticRepositoryMaintenanceTests : PublishingHistory
{
    [Fact]
    public void A_commit_and_a_push_start_no_maintenance()
    {
        var mainGitDir = Path.Join(Repo.Root, ".git");
        MakeRepackDueInTheForeground(mainGitDir);
        MakeRepackDueInTheForeground(OriginPath);
        foreach (var (index, content) in LooseObjectsInShard17(2).Index())
            Repo.Write($"src/Tasks/loose{index}.txt", content);

        CommitAndPush();

        Assert.Empty(Packs(mainGitDir));
        Assert.Empty(Packs(OriginPath));
    }

    /// <summary>
    /// Auto-maintenance repacks once git's loose-object estimate, the count of <c>objects/17</c> times 256, exceeds the
    /// configured threshold: the <c>gc</c> strategy reads <c>gc.auto</c>, the <c>geometric</c> strategy (git 2.55's
    /// default) reads <c>maintenance.geometric-repack.auto</c>. Either rounds 1 up to 256, so two objects in the shard
    /// are enough. Detaching is turned off under both names so the repack, if any, finishes before the command returns.
    /// </summary>
    private static void MakeRepackDueInTheForeground(string gitDir)
    {
        foreach (var (key, value) in new[] { ("gc.auto", "1"), ("maintenance.geometric-repack.auto", "1"), ("gc.autoDetach", "false"), ("maintenance.autoDetach", "false") })
            GitProcess.Run(gitDir, ["config", key, value]);
    }

    /// <summary>Distinct blob contents whose object ids start with <c>17</c>: git's loose-object estimate counts that shard alone.</summary>
    private static IEnumerable<string> LooseObjectsInShard17(int count)
    {
        for (var index = 0; count > 0; index++)
        {
            var content = $"loose object {index}";
            if (Convert.ToHexStringLower(SHA1.HashData(Encoding.ASCII.GetBytes($"blob {content.Length}\0{content}"))).StartsWith("17", StringComparison.Ordinal))
            {
                count--;
                yield return content;
            }
        }
    }

    private static IEnumerable<string> Packs(string gitDir) => Directory.EnumerateFiles(Path.Join(gitDir, "objects", "pack"), "*.pack");
}
