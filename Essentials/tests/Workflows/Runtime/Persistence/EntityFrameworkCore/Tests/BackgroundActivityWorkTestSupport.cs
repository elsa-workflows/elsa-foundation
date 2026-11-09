using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class BackgroundActivityWorkTestSupport
{
    public static async ValueTask<RuntimeExecutionLease> SeedLiveExecutionAsync(
        RuntimeDbContext context,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string workflowExecutionId,
        string tenantId,
        TimeProvider? timeProvider = null,
        string ownerId = "background-work-test-owner")
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var codec = new NoopContinuationCodec();
        await new EfWorkflowExecutionStateStore(context, accessContextAccessor, codec).SaveAsync(new WorkflowExecutionState(
            workflowExecutionId,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            now,
            now,
            now,
            null,
            null,
            null,
            tenantId,
            new Dictionary<string, string>()));

        var liveness = new EfExecutionLivenessStateStore(context, accessContextAccessor, codec);
        var ownership = new RuntimeExecutionOwnershipService(
            liveness,
            timeProvider ?? TimeProvider.System,
            new RuntimeExecutionOwnershipOptions { OwnerId = ownerId, LeaseDuration = TimeSpan.FromHours(1) });
        return await ownership.AcquireAsync(workflowExecutionId);
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }
}
