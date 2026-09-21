namespace Elsa.Primitives.Diagnostics;

/// <summary>
/// The <c>activity.*</c> error codes returned to API clients in problem-detail responses.
/// </summary>
/// <remarks>
/// <para>
/// These strings are a <b>wire contract</b>: clients match on them to decide how to react. A typo at any one call
/// site mints a code no client can match, and nothing detects it — the response still looks well formed. Naming them
/// once makes the set enumerable and a mistyped constant a compile error.
/// </para>
/// <para>
/// They live in <c>Elsa.Primitives</c> rather than a domain project because the same codes are raised from three
/// domains — Activities/Design, Workflows/Publishing and Workflows/Runtime. The apparently natural home,
/// <c>Elsa.Activities.Design.Core</c>, is not available: <c>Elsa.Workflows.Runtime</c> would have to reference it,
/// and the Design↔Runtime seam is enforced by <c>ArchitectureGuardTests</c>. <c>Elsa.Primitives</c> is
/// zero-dependency by §2.3 and satisfies the §2.17 shared-helper bar (three or more consumers, no new external
/// dependency).
/// </para>
/// <para>
/// Only the code is shared. The accompanying title and detail stay at each call site, because they describe the
/// specific failure rather than the category.
/// </para>
/// </remarks>
public static class ActivityErrorCodes
{
    /// <summary>The request payload failed validation.</summary>
    public const string RequestInvalid = "activity.request.invalid";

    /// <summary>The caller is not permitted to perform the requested action.</summary>
    public const string ActionForbidden = "activity.action.forbidden";

    /// <summary>The caller is not authorized for the requested activity.</summary>
    public const string AuthorizationDenied = "activity.authorization.denied";

    /// <summary>The operation failed for a reason that is not the caller's fault.</summary>
    public const string OperationFailed = "activity.operation.failed";

    /// <summary>The publication request is not valid for the target activity.</summary>
    public const string PublicationInvalid = "activity.publication.invalid";

    /// <summary>The publication conflicts with the activity's current published state.</summary>
    public const string PublicationConflict = "activity.publication.conflict";

    /// <summary>The requested version is not valid.</summary>
    public const string VersionInvalid = "activity.version.invalid";

    /// <summary>The requested version conflicts with the stored version.</summary>
    public const string VersionConflict = "activity.version.conflict";

    /// <summary>An activity provider failed while serving the request.</summary>
    public const string ProviderFailure = "activity.provider.failure";

    /// <summary>An activity provider is not currently available.</summary>
    public const string ProviderUnavailable = "activity.provider.unavailable";

    /// <summary>An activity provider reported a diagnostic condition.</summary>
    public const string ProviderDiagnostic = "activity.provider.diagnostic";

    /// <summary>The pagination cursor refers to a snapshot that is no longer retained.</summary>
    public const string CursorExpired = "activity.cursor.expired";

    /// <summary>Concurrent forks collided on the same activity.</summary>
    public const string ForkCollision = "activity.fork.collision";

    /// <summary>A declared activity dependency could not be resolved.</summary>
    public const string DependencyUnresolved = "activity.dependency.unresolved";

    /// <summary>The activity dependency graph contains a cycle.</summary>
    public const string DependencyCycle = "activity.dependency.cycle";
}
