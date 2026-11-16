using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// Where the clone lives (#2197): each holder of a source takes a clone slot of its own, a released slot is taken again
/// with its clone, so a Writer clone survives a restart, and an explicit path is used as given.
/// </summary>
public sealed class GitCloneSlotTests : GitExportTest
{
    private const UnixFileMode OwnerOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _slotsRoot;
    private readonly GitReconciliationOptions _source = new() { RemoteUrl = "git@example.com:acme/wf.git", Branch = "main" };

    public GitCloneSlotTests() => _slotsRoot = NewSlotsRoot();

    [Fact]
    public void Two_holders_of_one_source_take_different_slots()
    {
        var first = Slot(_source, _slotsRoot).RepositoryPath;
        var second = Slot(_source, _slotsRoot).RepositoryPath;

        Assert.Equal("slot-0", SlotName(first));
        Assert.Equal("slot-1", SlotName(second));
        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(first)), Path.GetDirectoryName(Path.GetDirectoryName(second)));
    }

    [Fact]
    public void A_released_slot_is_taken_again()
    {
        var first = Slot(_source, _slotsRoot);
        var held = first.RepositoryPath;
        var second = Slot(_source, _slotsRoot).RepositoryPath;

        first.Dispose();

        Assert.Equal(held, Slot(_source, _slotsRoot).RepositoryPath);
        Assert.NotEqual(held, second);
        Assert.Throws<ObjectDisposedException>(() => first.RepositoryPath);
    }

    [Fact]
    public void Distinct_sources_and_roles_never_share_a_slot()
    {
        var path = Slot(_source, _slotsRoot).RepositoryPath;

        Assert.NotEqual(path, Slot(new GitReconciliationOptions { RemoteUrl = "git@example.com:acme/other.git" }, _slotsRoot).RepositoryPath);
        Assert.NotEqual(path, Slot(new GitReconciliationOptions { RemoteUrl = _source.RemoteUrl, Branch = "release" }, _slotsRoot).RepositoryPath);
        // A Consumer resets its clone hard onto the remote, so it must never take over a Writer's clone.
        Assert.NotEqual(path, Slot(new GitReconciliationOptions { RemoteUrl = _source.RemoteUrl, Role = GitReconciliationRole.Writer }, _slotsRoot).RepositoryPath);
    }

    [Fact]
    public void An_explicit_clone_path_is_used_as_given_and_takes_no_slot()
    {
        var cache = Path.Join(Path.GetTempPath(), "explicit-clone");

        Assert.Equal(cache, Slot(new GitReconciliationOptions { RemoteUrl = _source.RemoteUrl, LocalCachePath = cache }, _slotsRoot).RepositoryPath);
        Assert.False(Directory.Exists(_slotsRoot));
    }

    [Fact]
    public async Task A_writer_clone_survives_a_restart_with_its_unpushed_export_commits()
    {
        Publish("wf-s", "S", "1.0.0");
        var before = Writer(GitPushMode.Manual, slotsRoot: _slotsRoot);
        await before.Exporter.ExportAsync(CancellationToken.None);
        var head = Head(before.CachePath);

        before.Slot.Dispose(); // the process ends
        var after = Writer(GitPushMode.Manual, slotsRoot: _slotsRoot);
        await after.Source.Read(CancellationToken.None);

        Assert.Equal(before.CachePath, after.CachePath);
        Assert.Equal(head, Head(after.CachePath));
        Assert.Contains("Publish S v1.0.0 (wf-s)", Subjects(after.CachePath));
        Assert.DoesNotContain(after.Git.Runs, run => run.Command == "clone");
    }

    [Fact]
    public void Slot_directories_are_the_users_alone()
    {
        if (OperatingSystem.IsWindows())
            return; // the user profile's ACL keeps other users out of its temp directory

        Directory.CreateDirectory(_slotsRoot, OwnerOnlyDirectory | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var slotDirectory = Path.GetDirectoryName(Slot(_source, _slotsRoot).RepositoryPath)!;

        Assert.Equal(OwnerOnlyDirectory, File.GetUnixFileMode(_slotsRoot)); // tightened, since it is this user's
        Assert.Equal(OwnerOnlyDirectory, File.GetUnixFileMode(Path.GetDirectoryName(slotDirectory)!));
        Assert.Equal(OwnerOnlyDirectory, File.GetUnixFileMode(slotDirectory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Join(slotDirectory, ".lock")));
    }

    [Fact]
    public void A_slot_path_through_a_symbolic_link_is_refused()
    {
        if (OperatingSystem.IsWindows())
            return;

        var target = NewSlotsRoot();
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(_slotsRoot, target);

        var refusal = Assert.Throws<InvalidOperationException>(() => Slot(_source, _slotsRoot).RepositoryPath);
        Assert.Contains("symbolic link", refusal.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public void The_default_root_is_the_runtime_directory_when_it_is_the_users_alone()
    {
        if (OperatingSystem.IsWindows())
            return;

        var runtime = NewSlotsRoot();
        Directory.CreateDirectory(runtime, OwnerOnlyDirectory);

        Assert.Equal(Path.Join(runtime, "elsa", "gitops"), GitCloneSlot.DefaultRoot(runtime, "/home/u/.local/share", "/tmp"));
    }

    [Fact]
    public void The_default_root_skips_a_runtime_directory_others_can_use()
    {
        if (OperatingSystem.IsWindows())
            return;

        var shared = NewSlotsRoot();
        Directory.CreateDirectory(shared, OwnerOnlyDirectory | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        var link = NewSlotsRoot();
        var own = NewSlotsRoot();
        Directory.CreateDirectory(own, OwnerOnlyDirectory);
        Directory.CreateSymbolicLink(link, own);
        var expected = Path.Join("/home/u/.local/share", "elsa", "gitops");

        Assert.Equal(expected, GitCloneSlot.DefaultRoot(shared, "/home/u/.local/share", "/tmp"));
        Assert.Equal(expected, GitCloneSlot.DefaultRoot(link, "/home/u/.local/share", "/tmp"));
        Assert.Equal(expected, GitCloneSlot.DefaultRoot(Path.Join(shared, "missing"), "/home/u/.local/share", "/tmp"));
        Assert.Equal(expected, GitCloneSlot.DefaultRoot("relative/run", "/home/u/.local/share", "/tmp"));
        Assert.Equal(expected, GitCloneSlot.DefaultRoot(null, "/home/u/.local/share", "/tmp"));
    }

    [Fact]
    public void The_default_root_falls_back_to_the_temp_dir_only_without_a_per_user_directory()
    {
        Assert.Equal(Path.Join("/tmp", "elsa-gitops"), GitCloneSlot.DefaultRoot(null, "", "/tmp"));
    }

    private static string SlotName(string repositoryPath) => Path.GetFileName(Path.GetDirectoryName(repositoryPath))!;
}
