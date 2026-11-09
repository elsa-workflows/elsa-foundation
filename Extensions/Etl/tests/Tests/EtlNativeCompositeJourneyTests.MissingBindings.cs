using Elsa.Activities.Runtime.Core.Models;
using Elsa.Etl.Core.Runtime;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Etl.Tests;

public sealed partial class EtlNativeCompositeJourneyTests
{
    [Fact]
    public Task Missing_binding_refusal_resumes_and_acknowledges_the_native_wait() =>
        WithJourneyAsync(CompositeKind.Sequence, NativeOutcome.Succeeded, async journey =>
        {
            var invocation = await journey.StartSuspendedAsync(expectTerminal: true);
            var refused = Assert.IsType<EtlRunView>(await journey.ReadRunAsync(invocation.Scope, invocation.RunId));
            Assert.Equal(EtlRunStatus.BusinessError, refused.Status);
            Assert.Equal("etl.binding-unavailable", refused.TerminalResult!.Diagnostic!.Code);
            Assert.Null(refused.Bindings);
            Assert.Null(refused.Prepared);
            Assert.Equal(0, refused.AcceptedRecords);
            Assert.Equal(0, refused.RejectedRecords);
            Assert.Equal(0, refused.FilteredRecords);
            Assert.Equal(0, refused.OutstandingRecords);
            Assert.Equal(0, refused.OutstandingBytes);
            Assert.Empty(await journey.ReadPartitionsAsync(invocation.Scope, invocation.RunId));
            Assert.Empty(await journey.ReadManifestAsync(invocation.Scope, invocation.RunId));
            Assert.NotNull(await journey.ReadBookmarkAsync(invocation.ResultAddress.BookmarkId));

            await journey.StartWorkerAsync();
            await journey.WaitForNativeResultAsync(invocation, ActivityExecutionStatus.Completed, WorkflowExecutionStatus.Completed);

            var native = await journey.ReadNativeStateAsync();
            var child = Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == journey.RunEtlNodeId);
            Assert.Equal(ActivityExecutionStatus.Completed, child.Status);
            AssertCompletionResult(child, refused);
            Assert.Equal(ActivityExecutionStatus.Completed,
                Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == journey.AfterEtlNodeId).Status);
            Assert.Equal(WorkflowExecutionStatus.Completed, native.Workflow.Status);
            await AssertConsumedDeliveryAsync(journey, invocation, child);
            var outbox = await journey.ReadOutboxAsync(invocation.Scope, invocation.RunId);
            Assert.NotNull(outbox.AcknowledgedAtUtcTicks);
        }, includeBindings: false);
}
