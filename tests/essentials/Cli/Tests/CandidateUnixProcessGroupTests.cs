using System.Runtime.InteropServices;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CandidateUnixProcessGroupTests
{
    [Fact]
    public async Task Waits_for_matching_executing_members_then_completes_on_nonexecution()
    {
        var snapshots = new Queue<CandidateUnixProcessGroupMember[]>([
            [new CandidateUnixProcessGroupMember(101, 42, true)],
            [new CandidateUnixProcessGroupMember(101, 42, false)]
        ]);
        var calls = 0;
        var observer = new CandidateUnixProcessGroup((group, _) =>
        {
            Assert.Equal(42, group);
            calls++;
            return snapshots.Dequeue();
        });

        await observer.WaitForExitAsync(42);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Ignores_members_from_another_group()
    {
        var observer = new CandidateUnixProcessGroup((_, _) =>
            [new CandidateUnixProcessGroupMember(101, 41, true)]);

        await observer.WaitForExitAsync(42);
    }

    [Fact]
    public async Task Treats_zombie_or_other_nonexecuting_members_as_complete()
    {
        var observer = new CandidateUnixProcessGroup((_, _) =>
            [
                new CandidateUnixProcessGroupMember(101, 42, false),
                new CandidateUnixProcessGroupMember(102, 42, false)
            ]);

        await observer.WaitForExitAsync(42);
    }

    [Fact]
    public async Task Cancellation_stops_a_pending_group_observation()
    {
        using var cancellation = new CancellationTokenSource();
        var firstSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new CandidateUnixProcessGroup((_, _) =>
        {
            firstSnapshot.TrySetResult();
            return [new CandidateUnixProcessGroupMember(101, 42, true)];
        });

        var pending = observer.WaitForExitAsync(42, cancellation.Token);
        await firstSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Propagates_snapshot_errors_instead_of_treating_them_as_exit()
    {
        var expected = new InvalidDataException("malformed snapshot");
        var observer = new CandidateUnixProcessGroup((_, _) => throw expected);

        var actual = await Assert.ThrowsAsync<InvalidDataException>(() => observer.WaitForExitAsync(42));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Native_reader_reports_the_current_process_group_on_supported_unix()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var group = Native.GetProcessGroupId();
        var members = CandidateUnixProcessGroup.ReadMembers(group).ToArray();

        Assert.Contains(members, member => member.ProcessId == Environment.ProcessId &&
                                            member.GroupId == group && member.IsExecuting);
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "getpgrp")]
        internal static extern int GetProcessGroupId();
    }
}
