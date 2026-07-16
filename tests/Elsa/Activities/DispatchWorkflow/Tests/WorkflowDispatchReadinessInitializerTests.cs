using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class WorkflowDispatchReadinessInitializerTests
{
    [Fact]
    public async Task InitializeAsync_AcceptsStoreWithAtomicDispatchCapabilities()
    {
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowDispatchStore, InMemoryWorkflowDispatchStore>()
            .AddSingleton<IWorkflowDispatchReadinessAssessor>(
                new WorkflowDispatchReadinessAssessor([]))
            .BuildServiceProvider();
        var initializer = new WorkflowDispatchReadinessInitializer(
            services,
            NullLogger<WorkflowDispatchReadinessInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InitializeAsync_RejectsStoreWithoutAtomicDispatchCapabilities()
    {
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowDispatchStore, LegacyDispatchStore>()
            .AddSingleton<IWorkflowDispatchReadinessAssessor>(
                new WorkflowDispatchReadinessAssessor([]))
            .BuildServiceProvider();
        var initializer = new WorkflowDispatchReadinessInitializer(
            services,
            NullLogger<WorkflowDispatchReadinessInitializer>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.InitializeAsync(CancellationToken.None));

        Assert.Contains(nameof(IWorkflowDispatchAdmissionStore), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IWorkflowDispatchCancellationStore), exception.Message, StringComparison.Ordinal);
    }

    private sealed class LegacyDispatchStore : IWorkflowDispatchStore
    {
        public ValueTask<WorkflowDispatchRecord> SaveAsync(
            WorkflowDispatchRecord record,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkflowDispatchRecord?> FindAsync(
            string dispatchId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(
            string parentWorkflowExecutionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
