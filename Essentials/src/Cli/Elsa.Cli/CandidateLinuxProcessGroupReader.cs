using System.Globalization;
using System.Text;

namespace Elsa.Cli;

/// <summary>Reads Linux process-group membership from procfs.</summary>
/// <remarks>
/// The optional delegates adapt the procfs directory enumeration and stat-file reads. The default
/// implementation reads the host's <c>/proc</c> tree.
/// </remarks>
public sealed class CandidateLinuxProcessGroupReader
{
    private const int LinuxErrnoNoSuchProcess = 3;

    private readonly Func<IEnumerable<string>> enumerateProcDirectories;
    private readonly Func<string, string> readStatFile;

    /// <summary>Creates a Linux procfs reader, optionally using a supplied procfs source.</summary>
    public CandidateLinuxProcessGroupReader(
        Func<IEnumerable<string>>? enumerateProcDirectories = null,
        Func<string, string>? readStatFile = null)
    {
        this.enumerateProcDirectories = enumerateProcDirectories ?? (() => Directory.EnumerateDirectories("/proc"));
        this.readStatFile = readStatFile ?? (path => File.ReadAllText(path, Encoding.ASCII));
    }

    /// <summary>Reads members of one process group from the configured Linux procfs source.</summary>
    /// <exception cref="InvalidDataException">A native process record is malformed or inconsistent.</exception>
    public IEnumerable<CandidateUnixProcessGroupMember> ReadMembers(
        int processGroupId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processGroupId);

        var members = new List<CandidateUnixProcessGroupMember>();
        foreach (var directory in enumerateProcDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var processId) || processId <= 0)
                continue;

            string stat;
            try
            {
                stat = readStatFile(Path.Combine(directory, "stat"));
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
            catch (IOException exception) when (exception.HResult == LinuxErrnoNoSuchProcess)
            {
                // .NET's Unix FileStream mapping stores the raw Linux errno in HResult; ESRCH means
                // the process disappeared while procfs was servicing the stat read.
                continue;
            }

            var snapshot = CandidateUnixProcessGroup.ParseLinuxMember(processId, stat);
            if (snapshot.GroupId == processGroupId)
                members.Add(snapshot);
        }

        return members;
    }
}
