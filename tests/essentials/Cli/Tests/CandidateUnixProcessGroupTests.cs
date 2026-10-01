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

    [Fact]
    public async Task Unavailable_snapshot_refuses_completion()
    {
        var observer = new CandidateUnixProcessGroup((_, _) => null!);
        await Assert.ThrowsAsync<InvalidDataException>(() => observer.WaitForExitAsync(42));
    }

    [Fact]
    public async Task Cancelled_empty_snapshot_does_not_report_completion()
    {
        using var cancellation = new CancellationTokenSource();
        var observer = new CandidateUnixProcessGroup((_, _) =>
        {
            cancellation.Cancel();
            return [];
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer.WaitForExitAsync(42, cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_group_is_refused(int group)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new CandidateUnixProcessGroup().WaitForExitAsync(group));
        Assert.Throws<ArgumentOutOfRangeException>(() => CandidateUnixProcessGroup.ReadMembers(group));
    }

    [Fact]
    public void Native_reader_observes_an_absent_group_as_empty()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Throws<PlatformNotSupportedException>(() => CandidateUnixProcessGroup.ReadMembers(int.MaxValue));
            return;
        }
        Assert.Empty(CandidateUnixProcessGroup.ReadMembers(int.MaxValue));
    }

    [Theory]
    [InlineData("R", true)]
    [InlineData("S", true)]
    [InlineData("D", true)]
    [InlineData("T", true)]
    [InlineData("t", true)]
    [InlineData("Z", false)]
    [InlineData("X", false)]
    [InlineData("x", false)]
    public void Linux_records_preserve_execution_state_and_group(string state, bool executing)
    {
        var member = CandidateUnixProcessGroup.ParseLinuxMember(321, Stat(state));
        Assert.Equal(new CandidateUnixProcessGroupMember(321, 42, executing), member);
    }

    [Fact]
    public void Linux_kernel_threads_with_zero_group_session_and_start_are_valid_unrelated_records()
    {
        Assert.Equal(new CandidateUnixProcessGroupMember(321, 0, true),
            CandidateUnixProcessGroup.ParseLinuxMember(321, Stat("S", "0", "0", "0")));
    }

    [Theory]
    [InlineData("321 (bad command S 1 42 42")]
    [InlineData("321(no separator) S 1 42 42")]
    [InlineData("322 (wrong pid) S 1 42 42")]
    [InlineData("321 (truncated) S 1 42")]
    public void Linux_malformed_or_mismatched_records_are_refused(string record)
    {
        Assert.Throws<InvalidDataException>(() => CandidateUnixProcessGroup.ParseLinuxMember(321, record));
    }

    [Theory]
    [InlineData("?", "42", "42", "987654")]
    [InlineData("S", "invalid", "42", "987654")]
    [InlineData("S", "-1", "42", "987654")]
    [InlineData("S", "42", "invalid", "987654")]
    [InlineData("S", "42", "-1", "987654")]
    [InlineData("S", "42", "42", "invalid")]
    [InlineData("S", "42", "42", "-1")]
    public void Linux_unknown_state_or_invalid_native_fields_are_refused(string state, string group, string session, string start)
    {
        Assert.Throws<InvalidDataException>(() => CandidateUnixProcessGroup.ParseLinuxMember(321, Stat(state, group, session, start)));
    }

    private static string Stat(string state, string group = "42", string session = "42", string start = "987654")
    {
        var fields = new string[20];
        Array.Fill(fields, "0");
        fields[0] = state;
        fields[2] = group;
        fields[3] = session;
        fields[19] = start;
        return $"321 (composer with spaces and ) parentheses) {string.Join(' ', fields)}";
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "getpgrp")]
        internal static extern int GetProcessGroupId();
    }
}
