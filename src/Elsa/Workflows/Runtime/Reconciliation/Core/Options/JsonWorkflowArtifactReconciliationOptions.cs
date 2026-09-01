namespace Elsa.Workflows.Runtime.Reconciliation.Core.Options;

/// <summary>One entry of an explicitly ordered set of closure files.</summary>
/// <param name="Order">Ascending read order. Ties are broken by ordinal file name so a pass stays deterministic.</param>
/// <param name="FilePath">The closure JSON file to read.</param>
public sealed record JsonWorkflowArtifactReconciliationFileOption(int Order, string FilePath);

/// <summary>
/// Options for the JSON closure-file reconciliation source: exactly one of a single <see cref="FilePath"/>, an
/// ordered set of <see cref="Files"/>, or a scanned <see cref="FolderPath"/>.
/// </summary>
/// <remarks>
/// Mirrors the design-side <c>JsonWorkflowReconciliationOptions</c>, including its non-recursive scan rationale, so
/// an operator who already mounts design catalogs configures artifact closures the same way. The exactly-one rule
/// is enforced by the feature at registration, not here, which keeps the source free of configuration policy.
/// </remarks>
public sealed class JsonWorkflowArtifactReconciliationOptions
{
    /// <summary>
    /// A single closure JSON file. The shorthand for the common one-file case; mutually exclusive with
    /// <see cref="Files"/> and <see cref="FolderPath"/> — configure exactly one of the three.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// An ordered set of closure files, read in ascending
    /// <see cref="JsonWorkflowArtifactReconciliationFileOption.Order"/>. Use this when the import must be staged —
    /// a closure is self-contained, so ordering matters only when the operator wants a deliberate sequence.
    /// Mutually exclusive with <see cref="FilePath"/> and <see cref="FolderPath"/>.
    /// </summary>
    public IEnumerable<JsonWorkflowArtifactReconciliationFileOption> Files { get; set; } = [];

    /// <summary>
    /// A directory scanned for <c>*.json</c> closure files — the mounted-volume/GitOps shape. The scan reads the
    /// folder's top level only (non-recursive, deliberately: mount implementations such as a Kubernetes ConfigMap's
    /// <c>..data</c> symlink tree would double-read files under recursion) in deterministic ordinal file-name
    /// order. A missing folder aborts the pass with an error naming this path; an empty folder — or one with no
    /// <c>*.json</c> matches — is logged and contributes nothing. Mutually exclusive with <see cref="FilePath"/>
    /// and <see cref="Files"/>.
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// The source identity recorded for this source. <b>Required</b>: a multi-file source has no single path to
    /// derive identity from, and this value is the activation ownership descriptor, so it must be chosen
    /// deliberately and kept stable across restarts.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// The tenant stamped on every source reference this source mints. <see langword="null"/> by default — the
    /// untenanted engine. Per-tenant fan-out (one mounted set activated once per tenant) is deferred (FR-B-002);
    /// today one source serves one tenant, so configure a second source to serve a second tenant.
    /// </summary>
    public string? TenantId { get; set; }
}
