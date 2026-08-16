using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Workflows.ExecutionEvidence.Authorization;

public static class ExecutionEvidencePermissionKeys
{
    public const string OwnerId = "Elsa.Workflows.ExecutionEvidence";
    public const string Read = "execution-evidence.read";
    public const string Delete = "execution-evidence.delete";
    public const string Manage = "execution-evidence.manage";
}

public sealed class ExecutionEvidencePermissionContributor : IPermissionContributor
{
    public string OwnerId => ExecutionEvidencePermissionKeys.OwnerId;

    public IEnumerable<Permission> Contribute() =>
    [
        new(ExecutionEvidencePermissionKeys.Read, "Read execution evidence", "Execution evidence", "Read workflow execution evidence pages and correlation queries."),
        new(ExecutionEvidencePermissionKeys.Delete, "Delete execution evidence", "Execution evidence", "Delete one workflow's execution evidence."),
        new(ExecutionEvidencePermissionKeys.Manage, "Manage execution evidence", "Execution evidence", "Delete and administer execution evidence.",
            new HashSet<string>(StringComparer.Ordinal)
            {
                ExecutionEvidencePermissionKeys.Delete,
                ExecutionEvidencePermissionKeys.Read
            })
    ];
}
