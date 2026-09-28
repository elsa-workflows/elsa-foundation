using System.Diagnostics;
using System.Text;

namespace Elsa.Versioning.Calculator;

/// <summary>What one git command returned: its exit code, its standard output as bytes, and its trimmed standard error.</summary>
public sealed record GitResult(int ExitCode, byte[] Output, string Error)
{
    /// <summary>Standard output as trimmed UTF-8 text.</summary>
    public string Text => Encoding.UTF8.GetString(Output).Trim();
}

/// <summary>
/// Runs one git command in a directory. It decides nothing about which commands may run: <see cref="GitRepository"/>
/// admits only read-only plumbing, and the publisher's record branch runs the commands that write it.
/// </summary>
public static class GitProcess
{
    /// <param name="directory">The directory git is run in, passed as <c>-C</c>.</param>
    /// <param name="arguments">The command and its arguments.</param>
    /// <param name="input">Bytes written to standard input, or null for none.</param>
    /// <param name="environment">Variables set for this command only, or null for none.</param>
    public static GitResult Run(string directory, IEnumerable<string> arguments, byte[]? input = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in (string[])["-C", directory, .. arguments])
            startInfo.ArgumentList.Add(argument);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");

        // Drain both pipes at once: reading one to the end first deadlocks when git fills the other's buffer.
        using var output = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var errorTask = process.StandardError.ReadToEndAsync();
        if (input is not null)
            process.StandardInput.BaseStream.Write(input);
        process.StandardInput.Close();
        process.WaitForExit();
        outputTask.GetAwaiter().GetResult();

        return new GitResult(process.ExitCode, output.ToArray(), errorTask.GetAwaiter().GetResult().Trim());
    }
}
