namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Applies a tenant-owned HTTP Connection to one outbound request using the existing destination policy.</summary>
public interface IWorkflowHttpConnectionApplier
{
    ValueTask ApplyAsync(HttpRequestMessage request, string name, string tenantId, IRuntimeSecretMask mask, string executionId,
        CancellationToken cancellationToken);
}
