using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Required_pin_with_capture_module_removed_refuses_before_invoke_or_parent_callback_effects(bool parent, bool required)
    {
        var native = CanonicalWorkflowStateTestData.EnsureRunning(new InMemoryWorkflowExecutionStateStore());
        var run = (await native.FindAsync("wfexec-1"))!;
        await native.SaveAsync(run with { HistoryCapturePolicy = required ? new("runtime-history-v3", 3, true, ["ActivityContinuationStarted"]) : null });
        var readerResolutions = 0;
        var typed = new RecordingTypedActivator();
        var structural = new FixedActivityActivator(new DeferringChildCompletionActivity());
        var executable = parent ? NewCallbackExecutable() : NewTypedExecutable();
        await _executableStore.SaveAsync(executable);
        var original = parent ? NewCallbackParentState(executable) : NewTypedRunningState(executable);
        await _activityStateStore.SaveAsync(original);
        if (parent) await _activityStateStore.SaveAsync(NewCompletedCallbackChildState());
        await using var provider = NewProvider(parent ? structural : typed, includeInspection: true, configure: services =>
        {
            services.AddSingleton<IWorkflowExecutionStateStore>(native);
            services.AddSingleton<IRuntimeHistoryStore>(_ => { readerResolutions++; throw new IOException("query-reader-must-not-resolve"); });
        });
        async Task Execute()
        {
            if (parent)
                await new WorkflowParentActivityCompletionSchedulerWorkHandler(provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(_now))
                    .HandleAsync(NewParentCallbackWorkItem("required-pin-callback", "required-pin-command"));
            else await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));
        }
        if (required)
        {
            var unavailable = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(Execute);
            Assert.Equal("runtime.history.capture-unavailable", unavailable.Code);
            Assert.Equal(original, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
            Assert.Empty(_checkpointWriter.ListCommits());
            Assert.Empty(await _incidentStateStore.ListAsync("wfexec-1"));
            Assert.Equal(0, parent ? structural.ActivateCalls : typed.ActivateCalls);
        }
        else
        {
            await Execute();
            Assert.Equal(1, parent ? structural.ActivateCalls : typed.ActivateCalls);
        }
        Assert.Equal(0, readerResolutions);
    }
}
