using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Checkpoint commit stores hold no structural rules: <c>RuntimeCheckpointCommitValidator</c> applies them in the
/// application layer, and every store trusts the commit it receives. That holds only while nothing hands a commit to a
/// store without passing the validator, so this guard fails closed on any new production file that names a checkpoint
/// commit store type, and on either validating call site dropping its validation.
/// </summary>
public sealed partial class RuntimeCheckpointCommitValidationBoundaryTests
{
    private const string Committer = "src/essentials/Workflows/Runtime/Services/Checkpoints/RuntimeCheckpointCommitter.cs";
    private const string CoalescingStore = "src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs";
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>
    /// Every production file allowed to name a checkpoint commit store type. Only <see cref="Committer"/> and
    /// <see cref="CoalescingStore"/> hand a commit to a store; the rest declare, implement, register, or document it.
    /// </summary>
    private static readonly string[] KnownReferences =
    [
        "src/essentials/Workflows/Runtime/Api/Coalescing/CoalescingRuntimeCheckpointPersistenceExtensions.cs",
        "src/essentials/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs",
        "src/essentials/Workflows/Runtime/Core/Contracts/RuntimeCheckpointCommitStoreBackend.cs",
        "src/essentials/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs",
        "src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/DependencyInjection/RuntimeCheckpointCommitEntityFrameworkCoreRegistration.cs",
        "src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeAlterationCheckpointParticipationGate.cs",
        "src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs",
        CoalescingStore,
        "src/essentials/Workflows/Runtime/Services/Checkpoints/InMemoryRuntimeCheckpointCommitStore.cs",
        "src/essentials/Workflows/Runtime/Services/Checkpoints/RuntimeCheckpointCommitValidator.cs",
        Committer,
        "src/essentials/Workflows/Runtime/Services/Dispatch/ScopedWorkflowDispatchRedriveStore.cs",
    ];

    [Fact]
    public void Only_known_production_files_name_a_checkpoint_commit_store_type()
    {
        var referencing = ModuleRoots.ProductionSourceFiles(RepoRoot)
            .Select(path => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
            .Where(path => CheckpointCommitStoreType().IsMatch(File.ReadAllText(Path.Join(RepoRoot, path))))
            .Order(StringComparer.Ordinal);

        Assert.Equal(KnownReferences.Order(StringComparer.Ordinal), referencing);
    }

    [Theory]
    [InlineData(Committer)]
    [InlineData(CoalescingStore)]
    public void A_call_site_that_hands_a_commit_to_a_store_validates_it(string path) =>
        Assert.Contains("RuntimeCheckpointCommitValidator.Validate(", File.ReadAllText(Path.Join(RepoRoot, path)), StringComparison.Ordinal);

    [GeneratedRegex(@"\b(IRuntimeCheckpointCommitStore|InMemoryRuntimeCheckpointCommitStore|EfRuntimeCheckpointCommitStore)\b")]
    private static partial Regex CheckpointCommitStoreType();

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
