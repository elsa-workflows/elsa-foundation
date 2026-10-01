using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Elsa.Cli.Tests;

internal readonly record struct ProcessIdentity(int Pid, long StartToken)
{
    public string ToMarker() => FormattableString.Invariant($"{Pid}|{StartToken}");
}

internal readonly record struct LinuxProcessSnapshot(ProcessIdentity Identity, char State);

internal static class ProcessIdentityReader
{
    private const string LinuxProcessStates = "RSDZTWXxKWPIN";

    public static ProcessIdentity Current()
    {
        using var process = Process.GetCurrentProcess();
        return Read(process.Id);
    }

    public static ProcessIdentity Read(int pid)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pid);

        if (OperatingSystem.IsLinux())
            return ReadLinux(pid).Identity;

        using var process = Process.GetProcessById(pid);
        return new ProcessIdentity(pid, process.StartTime.ToUniversalTime().Ticks);
    }

    public static LinuxProcessSnapshot ReadLinux(int pid)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pid);

        var path = $"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/stat";
        var stat = File.ReadAllText(path, Encoding.ASCII);
        return ParseLinuxStat(pid, stat);
    }

    internal static LinuxProcessSnapshot ParseLinuxStat(int expectedPid, string stat)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedPid);
        ArgumentException.ThrowIfNullOrEmpty(stat);

        var openParenthesis = stat.IndexOf('(');
        var closeParenthesis = stat.LastIndexOf(')');
        if (openParenthesis <= 0 || closeParenthesis <= openParenthesis || closeParenthesis + 2 >= stat.Length || stat[closeParenthesis + 1] != ' ')
            throw new InvalidDataException("The process stat record has an invalid command field.");

        var pidToken = stat.AsSpan(0, openParenthesis);
        if (pidToken[^1] != ' ')
            throw new InvalidDataException("The process stat record has an invalid PID field.");
        pidToken = pidToken[..^1];
        if (!int.TryParse(pidToken, NumberStyles.None, CultureInfo.InvariantCulture, out var actualPid) || actualPid != expectedPid)
            throw new InvalidDataException("The process stat record PID does not match the requested process.");

        var fields = stat[(closeParenthesis + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length <= 19 || fields[0].Length != 1 || !LinuxProcessStates.Contains(fields[0][0]) ||
            !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var startToken) || startToken <= 0)
            throw new InvalidDataException("The process stat record has an invalid state or start-time field.");

        return new LinuxProcessSnapshot(new ProcessIdentity(actualPid, startToken), fields[0][0]);
    }
}
