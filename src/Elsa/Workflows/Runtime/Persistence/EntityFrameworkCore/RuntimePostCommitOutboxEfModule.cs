using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema and bounded projection limits for the R20 post-commit outbox.</summary>
public static class RuntimePostCommitOutboxEfModule
{
    public const string TableName = "elsa_runtime_post_commit_outbox";
    public const string SchemaVersion = "1.0.0";
    public const int PhysicalIdentityMaximumLength = RuntimePostCommitOutboxIdentity.MaximumProjectionLength;
    // Workflow-execution auxiliary projection only; the unbounded logical OutboxItemId uses its full ordinal
    // text key and is deliberately absent from narrow-provider composite indexes.
    public const int PhysicalIdentityOrderPrefixMaximumLength = 32;
    public const int PhysicalIdentityOrderKeyMaximumLength =
        (PhysicalIdentityOrderPrefixMaximumLength + 1) * sizeof(char) * 2 + 64;
}
