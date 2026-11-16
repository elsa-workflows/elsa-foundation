using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Secrets.Workflows;

/// <summary>
/// Resolves a workflow activity's secret reference through the Secrets module's <see cref="ISecretValueResolver"/>,
/// so the workflow runtime reads secrets without referencing the Secrets module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Passed through unchanged.</b> The tenant is the one the runtime hands over, the execution partition read at
/// activation, and the reference's name, type name and scope reach the Secrets module as authored. Nothing here chooses a
/// tenant or a default.
/// </para>
/// <para>
/// <b>Classified here.</b> The failure codes belong to the Secrets module, so this bridge owns their meaning for a
/// workflow: <see cref="SecretResolutionFailureCode.StoreUnavailable"/> is transient and every other code is permanent.
/// The runtime copies the classification into the fault unchanged. A failed result that names no code or an undefined
/// one, a success that carries no value, and no result at all break the Secrets resolver's own result shape and are
/// reported as <see cref="SecretResolutionFailureCode.CorruptState"/>, permanent.
/// </para>
/// <para>
/// <b>Never the store's text.</b> Only the code name crosses into the runtime. This bridge never reads
/// <see cref="ResolvedSecret.Error"/>, whatever a resolver implementation puts there: a replacement
/// <see cref="ISecretValueResolver"/> could still put store-private detail, such as a connection target, in it. What the Secrets resolver throws is not caught here: activation drops it and
/// reports <c>ResolverFailed</c>.
/// </para>
/// </remarks>
public sealed class SecretValueRuntimeResolver(ISecretValueResolver secretValueResolver) : IRuntimeSecretResolver
{
    /// <inheritdoc />
    public async ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reference = request.Reference;
        var resolved = await secretValueResolver.ResolveAsync(
            request.TenantId,
            new SecretReference(reference.Name, reference.TypeName, reference.Scope),
            cancellationToken);

        return resolved switch
        {
            { Succeeded: true, Value: { } value } => RuntimeSecretResolution.Success(value),
            { Succeeded: false } failed => Classify(failed.FailureCode),
            _ => CorruptState
        };
    }

    /// <summary>
    /// The runtime failure for a Secrets failure code: its own name, retryable only for
    /// <see cref="SecretResolutionFailureCode.StoreUnavailable"/>. One arm per member, so each classification is read
    /// and reviewed on its own. A member added to the enum later falls to the last arm, permanent
    /// <see cref="SecretResolutionFailureCode.CorruptState"/>, until it gets an arm: the compiler does not catch it, the
    /// mapping theory in <c>SecretValueRuntimeResolverTests</c> does, because it has no expected row for the new member.
    /// </summary>
    private static RuntimeSecretResolution Classify(SecretResolutionFailureCode code) => code switch
    {
        SecretResolutionFailureCode.NotFound => Permanent(nameof(SecretResolutionFailureCode.NotFound)),
        SecretResolutionFailureCode.Inactive => Permanent(nameof(SecretResolutionFailureCode.Inactive)),
        SecretResolutionFailureCode.Expired => Permanent(nameof(SecretResolutionFailureCode.Expired)),
        SecretResolutionFailureCode.Revoked => Permanent(nameof(SecretResolutionFailureCode.Revoked)),
        SecretResolutionFailureCode.Deleted => Permanent(nameof(SecretResolutionFailureCode.Deleted)),
        SecretResolutionFailureCode.TypeMismatch => Permanent(nameof(SecretResolutionFailureCode.TypeMismatch)),
        SecretResolutionFailureCode.ScopeMismatch => Permanent(nameof(SecretResolutionFailureCode.ScopeMismatch)),
        SecretResolutionFailureCode.StoreUnavailable => RuntimeSecretResolution.Failure(nameof(SecretResolutionFailureCode.StoreUnavailable), isRetryable: true),
        SecretResolutionFailureCode.Unauthorized => Permanent(nameof(SecretResolutionFailureCode.Unauthorized)),
        SecretResolutionFailureCode.CorruptState => CorruptState,
        // A failed result that names no code breaks the Secrets resolver's own result shape.
        SecretResolutionFailureCode.None => CorruptState,
        // An undefined value, or a member without an arm above: a contract violation, never classified by its name.
        _ => CorruptState
    };

    private static RuntimeSecretResolution Permanent(string code) => RuntimeSecretResolution.Failure(code, isRetryable: false);

    private static RuntimeSecretResolution CorruptState => Permanent(nameof(SecretResolutionFailureCode.CorruptState));
}
