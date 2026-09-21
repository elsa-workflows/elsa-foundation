using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDispatchRedriveContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "request-1")]
    [InlineData("", "request-1")]
    [InlineData("dispatch-1", null)]
    [InlineData("dispatch-1", "")]
    public void Request_rejects_missing_provider_identity(string? dispatchId, string? requestId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WorkflowDispatchRedriveRequest(dispatchId!, requestId!, Now));
    }

    [Fact]
    public void Request_rejects_an_unrecorded_host_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowDispatchRedriveRequest("dispatch-1", "request-1", default));
    }

    [Fact]
    public void Request_rejects_oversized_or_control_character_operator_identity()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchRedriveRequest("dispatch-1", new string('r', WorkflowDispatchRedriveRequest.MaximumRequestIdLength + 1), Now));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchRedriveRequest("dispatch-1", "request\nsecret", Now));
    }

    [Fact]
    public void Result_is_bounded_to_safe_identifiers_and_lifecycle_fields()
    {
        var result = new WorkflowDispatchRedriveResult(
            "dispatch-1",
            WorkflowDispatchRedriveDisposition.AlreadyApplied,
            WorkflowDispatchStatus.Pending,
            generation: 3,
            incidentId: "incident-1",
            deadLetterId: "outbox-1");

        Assert.Equal("dispatch-1", result.DispatchId);
        Assert.Equal(WorkflowDispatchRedriveDisposition.AlreadyApplied, result.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Pending, result.Status);
        Assert.Equal(3, result.Generation);
        Assert.Equal("incident-1", result.IncidentId);
        Assert.Equal("outbox-1", result.DeadLetterId);
        Assert.Equal(
            new[] { "DeadLetterId", "DispatchId", "Disposition", "Generation", "IncidentId", "Status" },
            typeof(WorkflowDispatchRedriveResult).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Result_rejects_unsafe_or_invalid_shapes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowDispatchRedriveResult("dispatch-1", WorkflowDispatchRedriveDisposition.Accepted, WorkflowDispatchStatus.Pending, -1));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchRedriveResult("dispatch-1", WorkflowDispatchRedriveDisposition.Accepted, WorkflowDispatchStatus.Pending, 1, " "));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchRedriveResult("dispatch-1", WorkflowDispatchRedriveDisposition.Accepted, WorkflowDispatchStatus.Pending, 1, deadLetterId: " "));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchRedriveResult("dispatch-1", WorkflowDispatchRedriveDisposition.NotFound, WorkflowDispatchStatus.DispatchFailed, 1, "incident-1", "outbox-1"));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowDispatchRedriveResult("dispatch-1", WorkflowDispatchRedriveDisposition.NotEligible, status: null, generation: 0));
    }
}
