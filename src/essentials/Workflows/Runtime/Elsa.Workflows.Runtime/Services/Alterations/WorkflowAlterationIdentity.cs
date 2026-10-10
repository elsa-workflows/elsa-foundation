using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Derives stable, opaque identifiers from durable alteration identities without storing CLR type identity.</summary>
public static class WorkflowAlterationIdentity
{
    public static string CreatePlanId(string tenantPartition, string idempotencyKeyHash) => Create("plan", tenantPartition, idempotencyKeyHash);
    public static string CreateJobId(string planId, string workflowExecutionId) => Create("job", planId, workflowExecutionId);
    public static string CreateCheckpointCommitId(string jobId, int attempt) => Create("commit", jobId, attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Create(string prefix, params string[] parts)
    {
        if (parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Alteration identity parts cannot be blank.", nameof(parts));
        var bytes = Encoding.UTF8.GetBytes(string.Join("\u001f", [prefix, .. parts]));
        return $"alteration-{prefix}-{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}";
    }
}
