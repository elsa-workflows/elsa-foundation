extern alias FakeWorkerHost;

using Elsa.Cli.Worker;
using FakeContextHost = FakeWorkerHost::Elsa.Cli.Fixtures.WorkerContextHost.FakeContextHost;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class WorkerRunnerConfigurationContextTests
{
    [Fact]
    public async Task Cancellation_reaches_the_v2_operation_and_worker_runner_disposes_its_context_once()
    {
        FakeContextHost.Reset();
        using var cancellation = new CancellationTokenSource();
        FakeContextHost.ExpectedCancellationToken = cancellation.Token;

        var run = WorkerRunner.ExecuteExplicitContextAsync(
            CreateToolingEntryPoint(), Request(), WorkerCommands.List,
            HostDepsFile.Read(Path.ChangeExtension(typeof(WorkerRunnerConfigurationContextTests).Assembly.Location, ".deps.json")),
            NuplanePackageSet.None, cancellation.Token);

        await FakeContextHost.OperationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, FakeContextHost.ContextsCreated);
        Assert.Equal(1, FakeContextHost.OperationsInvoked);
        Assert.Equal(1, FakeContextHost.ContextsDisposed);
        Assert.True(FakeContextHost.FactoryReceivedExpectedToken);
        Assert.True(FakeContextHost.OperationReceivedExpectedToken);
        Assert.Equal(2, FakeContextHost.OperationVersion);
        Assert.Equal(WorkerCommands.List, FakeContextHost.OperationCommand);
    }

    [Fact]
    public async Task Host_refusal_disposes_the_created_context_once()
    {
        FakeContextHost.Reset();
        FakeContextHost.ReturnRefusal = true;

        var response = await WorkerRunner.ExecuteExplicitContextAsync(
            CreateToolingEntryPoint(), Request(), WorkerCommands.List,
            HostDepsFile.Read(Path.ChangeExtension(typeof(WorkerRunnerConfigurationContextTests).Assembly.Location, ".deps.json")),
            NuplanePackageSet.None, CancellationToken.None);

        Assert.Equal(ToolExitCode.Refusal, response.ExitCode);
        Assert.Equal("host-refused", response.Tooling!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, FakeContextHost.ContextsCreated);
        Assert.Equal(1, FakeContextHost.OperationsInvoked);
        Assert.Equal(1, FakeContextHost.ContextsDisposed);
        Assert.Equal(2, FakeContextHost.OperationVersion);
        Assert.Equal(WorkerCommands.List, FakeContextHost.OperationCommand);
    }

    private static ToolingEntryPoint CreateToolingEntryPoint()
    {
        var host = typeof(FakeContextHost);
        var context = typeof(FakeWorkerHost::Elsa.Persistence.EntityFramework.Tooling.EfToolingConfigurationContext);
        var contract = typeof(FakeWorkerHost::Elsa.Persistence.EntityFramework.Tooling.EfToolingContextContract);
        var contextApi = ToolingEntryPoint.BindContextApi(host, context, contract)!;

        return new ToolingEntryPoint(
            host.GetMethod("LegacyRunAsync")!,
            host.GetMethod("ProviderPackageId")!,
            host.GetMethod("DescribeBindingFailure")!,
            host.GetMethod("Select")!,
            supportsCapabilitySelection: false,
            contextApi);
    }

    private static WorkerRequest Request() => new()
    {
        Command = WorkerCommands.List,
        HostDirectory = "/fixture",
        HostName = "Fixture.Host",
        DepsFile = Path.ChangeExtension(typeof(WorkerRunnerConfigurationContextTests).Assembly.Location, ".deps.json"),
        Environment = "Production",
        ContextVersion = 1,
        ContextSource = WorkerContextSources.WorkbenchJson,
        Shell = "default",
        Resource = "primary"
    };
}
