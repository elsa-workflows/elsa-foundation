using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Elsa.Cli.Worker;

namespace Elsa.Cli.Tests;

internal sealed record PseudoTerminalCliRun(int ExitCode, string Output, string Error, bool ResponseSent, bool TimedOut);

/// <summary>Runs the built CLI with a real terminal on stdin/stdout/stderr and answers one reviewed prompt.</summary>
internal static class PseudoTerminalCli
{
    private static readonly string s_pythonRunner = """
        import base64, errno, os, pty, select, signal, sys, termios, time

        timeout_seconds = float(sys.argv[1])
        expected_prompt = base64.b64decode(sys.argv[2])
        response = base64.b64decode(sys.argv[3]) + b"\n"
        mutation_path = base64.b64decode(sys.argv[4]).decode("utf-8")
        mutation_content = base64.b64decode(sys.argv[5])
        command = sys.argv[6:]
        try:
            pid, master = pty.fork()
        except (OSError, AttributeError, NotImplementedError):
            sys.stderr.write("PTY_UNSUPPORTED: Python pty.fork is unavailable.\n")
            raise SystemExit(125)

        if pid == 0:
            try:
                settings = termios.tcgetattr(0)
                settings[3] &= ~termios.ECHO
                termios.tcsetattr(0, termios.TCSANOW, settings)
                os.execvp(command[0], command)
            except BaseException:
                os.write(2, b"\r\nPTY_EXEC_FAILED\n")
                os._exit(126)

        captured = bytearray()
        response_sent = False
        deadline = time.monotonic() + timeout_seconds
        child_status = None
        timed_out = False
        while child_status is None:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                timed_out = True
                try:
                    os.kill(pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                grace_deadline = time.monotonic() + 0.5
                while time.monotonic() < grace_deadline:
                    finished, child_status = os.waitpid(pid, os.WNOHANG)
                    if finished:
                        break
                    time.sleep(0.02)
                if child_status is None:
                    try:
                        os.kill(pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    _, child_status = os.waitpid(pid, 0)
                break

            readable, _, _ = select.select([master], [], [], min(0.05, remaining))
            if readable:
                try:
                    chunk = os.read(master, 4096)
                    if not chunk:
                        break
                    captured.extend(chunk)
                except OSError as error:
                    if error.errno != errno.EIO:
                        raise

            if not response_sent and expected_prompt in captured:
                try:
                    if mutation_path:
                        with open(mutation_path, "wb") as file:
                            file.write(mutation_content)
                    os.write(master, response)
                    response_sent = True
                except OSError:
                    pass

            finished, status = os.waitpid(pid, os.WNOHANG)
            if finished:
                child_status = status

        # Drain any text written just before exit.
        while True:
            readable, _, _ = select.select([master], [], [], 0)
            if not readable:
                break
            try:
                chunk = os.read(master, 4096)
                if not chunk:
                    break
                captured.extend(chunk)
            except OSError as error:
                if error.errno == errno.EIO:
                    break
                raise

        os.close(master)
        sys.stdout.buffer.write(captured)
        sys.stdout.buffer.flush()
        if response_sent:
            sys.stderr.write("PTY_RESPONSE_SENT\n")
        if timed_out:
            sys.stderr.write("PTY_TIMEOUT: CLI did not finish before the test deadline.\n")
            raise SystemExit(124)
        if child_status is None:
            child_status = os.waitpid(pid, 0)[1]
        exit_code = os.waitstatus_to_exitcode(child_status)
        raise SystemExit(exit_code if exit_code >= 0 else 128 - exit_code)
        """;

    public static Task<PseudoTerminalCliRun> RunElsaAsync(
        string expectedPrompt,
        string reviewResponse,
        IReadOnlyList<string> cliArguments,
        TimeSpan? timeout = null,
        string? beforeResponsePath = null,
        string? beforeResponseText = null) =>
        RunProcessAsync(
            DotnetMuxer.Path(),
            ["exec", DotnetElsa.ToolAssembly, .. cliArguments],
            expectedPrompt,
            reviewResponse,
            timeout,
            beforeResponsePath,
            beforeResponseText);

    /// <summary>Runs a process through the same PTY bridge; exposed internally for a small harness self-test.</summary>
    internal static async Task<PseudoTerminalCliRun> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string expectedPrompt,
        string reviewResponse,
        TimeSpan? timeout = null,
        string? beforeResponsePath = null,
        string? beforeResponseText = null)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Interactive CLI tests require a Unix PTY; Windows is not supported.");
        ArgumentException.ThrowIfNullOrEmpty(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrEmpty(expectedPrompt);
        ArgumentNullException.ThrowIfNull(reviewResponse);
        if (reviewResponse.Contains('\r') || reviewResponse.Contains('\n'))
            throw new ArgumentException("The review response must be one line.", nameof(reviewResponse));
        if ((beforeResponsePath is null) != (beforeResponseText is null))
            throw new ArgumentException("Both pre-response mutation values must be supplied together.");

        var deadline = timeout ?? TimeSpan.FromSeconds(30);
        if (deadline <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var startInfo = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(s_pythonRunner);
        startInfo.ArgumentList.Add(deadline.TotalSeconds.ToString("R", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(expectedPrompt)));
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(reviewResponse)));
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(beforeResponsePath ?? string.Empty)));
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(beforeResponseText ?? string.Empty)));
        startInfo.ArgumentList.Add(Path.IsPathRooted(executable) ? Path.GetFullPath(executable) : executable);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = StartPython(startInfo);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (error.Contains("PTY_UNSUPPORTED:", StringComparison.Ordinal))
            throw new PlatformNotSupportedException("Python 3 could not create the required interactive PTY.");
        if (output.Contains("PTY_EXEC_FAILED", StringComparison.Ordinal))
            throw new InvalidOperationException("The command could not be started inside the interactive PTY.");
        var responseSent = error.Contains("PTY_RESPONSE_SENT", StringComparison.Ordinal);
        error = error.Replace("PTY_RESPONSE_SENT\n", "", StringComparison.Ordinal);
        var timedOut = error.Contains("PTY_TIMEOUT:", StringComparison.Ordinal);
        error = error.Replace("PTY_TIMEOUT: CLI did not finish before the test deadline.\n", "", StringComparison.Ordinal);
        return new PseudoTerminalCliRun(
            process.ExitCode,
            output,
            error,
            responseSent,
            timedOut);
    }

    private static Process StartPython(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException("Python 3 did not start the PTY runner.");
        }
        catch (Win32Exception exception)
        {
            throw new PlatformNotSupportedException("Interactive CLI tests require Python 3 (`python3`) on PATH.", exception);
        }
    }
}
