using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema and bounded projection limits for the R20 post-commit outbox.</summary>
public static class RuntimePostCommitOutboxEfModule
{
    public const string TableName = "elsa_runtime_post_commit_outbox";
    public const string SchemaVersion = "1.0.0";
    public const int PhysicalIdentityMaximumLength = RuntimePostCommitOutboxIdentity.MaximumProjectionLength;
    // The full ordinal UTF-16 key would be too wide for a cross-provider composite index at the
    // 256-code-unit projection bound. Keep a fixed ordinal prefix and append a full-value digest;
    // the row id remains the final deterministic tie-breaker.
    public const int PhysicalIdentityOrderPrefixMaximumLength = 32;
    public const int PhysicalIdentityOrderKeyMaximumLength =
        (PhysicalIdentityOrderPrefixMaximumLength + 1) * sizeof(char) * 2 + 64;
}
