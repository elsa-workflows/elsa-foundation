using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>Protects deferred alteration payloads with tenant- and request-bound authenticated encryption.</summary>
public interface IWorkflowAlterationPayloadProtector
{
    /// <summary>Encrypts one canonical request payload for a durable plan.</summary>
    ProtectedWorkflowAlterationPayload Protect(string planId, string tenantPartition, string canonicalRequestHash, string plaintext);

    /// <summary>Decrypts one payload only when all associated plan, tenant, and request facts match.</summary>
    string Unprotect(string planId, string tenantPartition, string canonicalRequestHash, ProtectedWorkflowAlterationPayload payload);
}
