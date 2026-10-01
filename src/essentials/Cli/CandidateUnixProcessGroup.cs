using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Elsa.Cli;

/// <summary>Observes whether a Unix process group still contains an executing process.</summary>
/// <remarks>
/// This type only observes a group. It never sends a signal and never follows parent/child
/// relationships after a process has been reparented. Callers own termination and provide the
/// cancellation budget for the bounded observation.
/// </remarks>
public sealed class CandidateUnixProcessGroup
{
    private const int MacShortBsdInfoFlavor = 13;
    private const int MacShortBsdInfoSize = 64;
    private const int MacZombieState = 5;
    private const int InitialMacProcessCapacity = 32;
    private const int MaximumMacProcessCapacity = 1 << 16;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private const string LinuxProcessStates = "RSDZTtWXxKPI";

    private readonly Func<int, CancellationToken, IEnumerable<CandidateUnixProcessGroupMember>> readMembers;

    /// <summary>Creates an observer, optionally replacing native group snapshots for deterministic tests.</summary>
    public CandidateUnixProcessGroup(
        Func<int, CancellationToken, IEnumerable<CandidateUnixProcessGroupMember>>? readMembers = null)
    {
        this.readMembers = readMembers ?? ReadMembers;
    }

    /// <summary>
    /// Waits until the specified group contains no process that is still executing.
    /// </summary>
    /// <exception cref="InvalidDataException">A native process record is malformed or inconsistent.</exception>
    public async Task WaitForExitAsync(int processGroupId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processGroupId);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = readMembers(processGroupId, cancellationToken)
                          ?? throw new InvalidDataException("The process-group snapshot was unavailable.");
            var executing = false;
            foreach (var member in members)
            {
                if (member.GroupId != processGroupId)
                    continue;
                if (member.IsExecuting)
                {
                    executing = true;
                    break;
                }
            }

            if (!executing)
                return;

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the current native members of one process group on Linux or macOS.</summary>
    public static IEnumerable<CandidateUnixProcessGroupMember> ReadMembers(
        int processGroupId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processGroupId);
        if (OperatingSystem.IsLinux())
            return ReadLinuxMembers(processGroupId, cancellationToken);
        if (OperatingSystem.IsMacOS())
            return ReadMacMembers(processGroupId, cancellationToken);
        throw new PlatformNotSupportedException();
    }

    private static IEnumerable<CandidateUnixProcessGroupMember> ReadLinuxMembers(
        int processGroupId, CancellationToken cancellationToken)
    {
        var members = new List<CandidateUnixProcessGroupMember>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId <= 0)
                continue;

            LinuxProcessSnapshot snapshot;
            try
            {
                snapshot = ReadLinuxStat(processId, File.ReadAllText(Path.Combine(directory, "stat"), Encoding.ASCII));
            }
            catch (FileNotFoundException)
            {
                // The process exited between directory enumeration and stat read.
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                // The process directory disappeared between directory enumeration and stat read.
                continue;
            }

            if (snapshot.GroupId == processGroupId)
                members.Add(new CandidateUnixProcessGroupMember(snapshot.ProcessId, snapshot.GroupId, snapshot.IsExecuting));
        }

        return members;
    }

    private static LinuxProcessSnapshot ReadLinuxStat(int expectedProcessId, string stat)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedProcessId);
        ArgumentException.ThrowIfNullOrEmpty(stat);

        var openParenthesis = stat.IndexOf('(');
        var closeParenthesis = stat.LastIndexOf(')');
        if (openParenthesis <= 0 || closeParenthesis <= openParenthesis || closeParenthesis + 2 >= stat.Length ||
            stat[closeParenthesis + 1] != ' ')
            throw new InvalidDataException("The process stat record has an invalid command field.");

        if (!int.TryParse(stat.AsSpan(0, openParenthesis).TrimEnd(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var actualProcessId) || actualProcessId != expectedProcessId)
            throw new InvalidDataException("The process stat record PID does not match the requested process.");

        var fields = stat[(closeParenthesis + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length <= 19 || fields[0].Length != 1 || !LinuxProcessStates.Contains(fields[0][0]) ||
            !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var groupId) || groupId <= 0 ||
            !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId) || sessionId <= 0 ||
            !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var startToken) || startToken <= 0)
            throw new InvalidDataException("The process stat record has an invalid state, group, session, or start-time field.");

        return new LinuxProcessSnapshot(actualProcessId, groupId, sessionId, startToken,
            fields[0][0] is not ('Z' or 'X' or 'x'));
    }

    private static IEnumerable<CandidateUnixProcessGroupMember> ReadMacMembers(
        int processGroupId, CancellationToken cancellationToken)
    {
        var capacity = InitialMacProcessCapacity;
        int count;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffer = Marshal.AllocHGlobal(checked(capacity * sizeof(int)));
                var result = MacNative.ProcListProcessGroupIds(processGroupId, buffer, capacity * sizeof(int));
                if (result < 0)
                    throw new IOException("The native process-group enumeration failed.");
                if (result > capacity)
                    throw new InvalidDataException("The native process-group enumeration returned an invalid count.");
                if (result < capacity)
                {
                    count = result;
                    break;
                }

                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                if (capacity >= MaximumMacProcessCapacity)
                    throw new InvalidDataException("The native process-group enumeration exceeded its bounded capacity.");
                capacity = checked(capacity * 2);
            }

            var members = new List<CandidateUnixProcessGroupMember>(count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processId = Marshal.ReadInt32(buffer, index * sizeof(int));
                if (processId <= 0)
                    throw new InvalidDataException("The native process-group enumeration returned an invalid process ID.");

                var info = Marshal.AllocHGlobal(MacShortBsdInfoSize);
                try
                {
                    var bytes = MacNative.ProcProcessInfo(processId, MacShortBsdInfoFlavor, 0, info, MacShortBsdInfoSize);
                    if (bytes != MacShortBsdInfoSize)
                        throw new IOException("The native process information query failed.");

                    var actualProcessId = Marshal.ReadInt32(info, 0);
                    var groupId = Marshal.ReadInt32(info, sizeof(int) * 2);
                    var state = Marshal.ReadInt32(info, sizeof(int) * 3);
                    if (actualProcessId != processId || groupId <= 0 || state is < 1 or > 5)
                        throw new InvalidDataException("The native process information was inconsistent.");
                    if (groupId != processGroupId)
                        throw new InvalidDataException("The native process information changed process groups.");

                    members.Add(new CandidateUnixProcessGroupMember(processId, groupId, state != MacZombieState));
                }
                finally
                {
                    Marshal.FreeHGlobal(info);
                }
            }

            return members;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    private readonly record struct LinuxProcessSnapshot(
        int ProcessId, int GroupId, int SessionId, long StartToken, bool IsExecuting);

    private static class MacNative
    {
        [DllImport("libproc.dylib", EntryPoint = "proc_listpgrppids", SetLastError = true)]
        internal static extern int ProcListProcessGroupIds(int processGroupId, IntPtr buffer, int bufferSize);

        [DllImport("libproc.dylib", EntryPoint = "proc_pidinfo", SetLastError = true)]
        internal static extern int ProcProcessInfo(int processId, int flavor, ulong argument, IntPtr buffer, int bufferSize);
    }
}

/// <summary>A single native member observed in a Unix process group.</summary>
public readonly record struct CandidateUnixProcessGroupMember(int ProcessId, int GroupId, bool IsExecuting);
