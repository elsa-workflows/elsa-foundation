using System.Diagnostics;
using Xunit;

namespace Elsa.Workbench.Tests;

[Collection(WorkbenchBackgroundActivityRecoveryTests.Name)]
public sealed class WorkbenchProcessTests
{
    [SkippableTheory]
    [InlineData("graceful")]
    [InlineData("crash")]
    [InlineData("dispose")]
    public async Task Suspended_owned_host_can_resume_and_exit_without_leaving_its_directory(string exitMode)
    {
        Skip.If(OperatingSystem.IsWindows(), "The fixture suspends its owned host with POSIX signals.");
        string directory;
        int processId;
        int supervisorProcessId;
        await using (var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development))
        {
            directory = host.ContentRoot;
            processId = host.ProcessId;
            supervisorProcessId = host.SupervisorProcessId;
            Assert.NotEqual(host.SupervisorProcessId, processId);
            await host.SuspendAsync();
            Assert.False(host.HasExited);

            await host.ResumeAsync();
            using var response = await host.Client.GetAsync("/health/ready");
            response.EnsureSuccessStatusCode();
            Assert.Equal(processId, host.ProcessId);

            await host.SuspendAsync();
            if (exitMode == "graceful")
            {
                Assert.NotNull(await host.StopAsync(TimeSpan.FromMinutes(1)));
                Assert.Equal(0, host.ExitCode);
            }
            else if (exitMode == "crash")
            {
                await host.CrashAsync();
                await host.CrashAsync();
            }
            if (exitMode != "dispose")
                Assert.True(host.HasExited);
        }
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(supervisorProcessId));
        Assert.False(Directory.Exists(directory));
    }
}
