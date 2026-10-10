using System.Diagnostics;
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

    [Fact]
    public void Linux_reader_skips_a_vanished_entry_and_retains_a_later_live_group_member()
    {
        var reader = new CandidateLinuxProcessGroupReader(
            enumerateProcDirectories: () => ["/proc/101", "/proc/102"],
            readStatFile: path => path switch
            {
                var value when value == Path.Combine("/proc/101", "stat") => throw new IOException("No such process", 3),
                var value when value == Path.Combine("/proc/102", "stat") => Stat(102, "R", "42", "42", "987654"),
                _ => throw new InvalidOperationException($"Unexpected stat path: {path}")
            });

        var members = reader.ReadMembers(42).ToArray();

        Assert.Equal([new CandidateUnixProcessGroupMember(102, 42, true)], members);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(unchecked((int)0x80070003))]
    public void Linux_reader_propagates_non_esrch_io_errors(int hresult)
    {
        var expected = new IOException("A different I/O failure", hresult);
        var reader = new CandidateLinuxProcessGroupReader(
            enumerateProcDirectories: () => ["/proc/101"],
            readStatFile: _ => throw expected);

        var actual = Assert.Throws<IOException>(() => reader.ReadMembers(42).ToArray());

        Assert.Same(expected, actual);
        Assert.Equal(hresult, actual.HResult);
    }

    [Fact]
    public void Linux_reader_does_not_hide_malformed_records()
    {
        var reader = new CandidateLinuxProcessGroupReader(
            enumerateProcDirectories: () => ["/proc/101"],
            readStatFile: _ => "malformed stat record");

        Assert.Throws<InvalidDataException>(() => reader.ReadMembers(42).ToArray());
    }

    [Fact]
    public void Linux_reader_does_not_hide_cancellation_or_directory_enumeration_errors()
    {
        using var cancellation = new CancellationTokenSource();
        var cancellingReader = new CandidateLinuxProcessGroupReader(
            enumerateProcDirectories: () => ["/proc/101", "/proc/102"],
            readStatFile: _ =>
            {
                cancellation.Cancel();
                return Stat(101, "R", "42", "42", "987654");
            });

        Assert.ThrowsAny<OperationCanceledException>(() => cancellingReader.ReadMembers(42, cancellation.Token).ToArray());

        var expected = new IOException("Enumeration failed", 3);
        var enumerationReader = new CandidateLinuxProcessGroupReader(
            enumerateProcDirectories: () => throw expected,
            readStatFile: _ => throw new InvalidOperationException("The stat reader should not run."));

        var actual = Assert.Throws<IOException>(() => enumerationReader.ReadMembers(42).ToArray());
        Assert.Same(expected, actual);
    }

    [Fact]
    public void Linux_reader_still_skips_missing_stat_files_and_directories()
    {
        var reader = new CandidateLinuxProcessGroupReader(
            enumerateProcDirectories: () => ["/proc/101", "/proc/102", "/proc/103"],
            readStatFile: path => path switch
            {
                var value when value == Path.Combine("/proc/101", "stat") => throw new FileNotFoundException(),
                var value when value == Path.Combine("/proc/102", "stat") => throw new DirectoryNotFoundException(),
                var value when value == Path.Combine("/proc/103", "stat") => Stat(103, "S", "42", "42", "987654"),
                _ => throw new InvalidOperationException($"Unexpected stat path: {path}")
            });

        var members = reader.ReadMembers(42).ToArray();

        Assert.Equal([new CandidateUnixProcessGroupMember(103, 42, true)], members);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Linux_reader_rejects_invalid_group_before_reading_procfs(int group)
    {
        var reader = new CandidateLinuxProcessGroupReader(
            () => throw new InvalidOperationException("Enumeration must not run."));

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadMembers(group));
    }

    [Fact]
    public void Linux_reader_ignores_non_pid_entries_and_other_groups()
    {
        var readPaths = new List<string>();
        var reader = new CandidateLinuxProcessGroupReader(
            () => ["/proc/self", "/proc/0", "/proc/-1", "/proc/321"],
            path =>
            {
                readPaths.Add(path);
                return Stat("R", group: "43");
            });

        Assert.Empty(reader.ReadMembers(42));
        Assert.Equal([Path.Combine("/proc/321", "stat")], readPaths);
    }

    [Fact]
    public async Task Linux_proc_stat_descriptor_reports_esrch_after_its_owned_child_is_reaped()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var child = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false });
        Assert.NotNull(child);

        try
        {
            using var stat = File.OpenRead($"/proc/{child.Id}/stat");
            child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            var exception = Assert.Throws<IOException>(() => stat.ReadByte());
            Assert.Equal(3, exception.HResult);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static string Stat(string state, string group = "42", string session = "42", string start = "987654") =>
        Stat(321, state, group, session, start);

    private static string Stat(int processId, string state, string group, string session, string start)
    {
        var fields = new string[20];
        Array.Fill(fields, "0");
        fields[0] = state;
        fields[2] = group;
        fields[3] = session;
        fields[19] = start;
        return $"{processId} (composer with spaces and ) parentheses) {string.Join(' ', fields)}";
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "getpgrp")]
        internal static extern int GetProcessGroupId();
    }
}
