using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// A workflow test run, of a version or of an unsaved draft snapshot, with member reads off (the default), refuses
/// every member-read kind, a result member read included, and a member path on a whole-value kind before it saves an
/// executable or dispatches. The rejection reason carries only the exception's generic text: the finding code and
/// input path are not in the test-run view.
/// </summary>
public sealed partial class WorkflowTestRunRequestHandlerTests
{
    [Theory]
    [MemberData(nameof(PublishWorkflowRequestHandlerTests.UnsupportedMemberReads), MemberType = typeof(PublishWorkflowRequestHandlerTests))]
    public async Task A_version_test_run_refuses_a_member_read_before_saving_an_executable(string shape, string finding)
    {
        var handler = Handler(WorkflowVersion(Node("consumer", PublishWorkflowRequestHandlerTests.MemberRead(shape))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Null(view.ArtifactId);
        AssertGenericDataReferenceReason(view.Reason, finding);
        Assert.Empty(await _executableStore.ListAllAsync());
    }

    [Theory]
    [MemberData(nameof(PublishWorkflowRequestHandlerTests.UnsupportedMemberReads), MemberType = typeof(PublishWorkflowRequestHandlerTests))]
    public async Task A_draft_snapshot_test_run_refuses_a_member_read_before_saving_an_executable(string shape, string finding)
    {
        var handler = DraftSnapshotHandler();

        var view = await handler.Handle(new StartWorkflowDraftTestRun("definition-1", "snapshot-member-read",
            new WorkflowDefinitionState([], Node("consumer", PublishWorkflowRequestHandlerTests.MemberRead(shape)), [], [], null)), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Null(view.ArtifactId);
        AssertGenericDataReferenceReason(view.Reason, finding);
        Assert.Empty(await _executableStore.ListAllAsync());
    }

    // The analyzer's findings are not carried into the test-run view; only the exception's generic message is.
    private static void AssertGenericDataReferenceReason(string? reason, string finding)
    {
        Assert.NotNull(reason);
        Assert.StartsWith("One or more activity data references are invalid.", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(finding, reason, StringComparison.Ordinal);
    }
}
