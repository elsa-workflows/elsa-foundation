namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// One provider leaf's admission of one named target: inspect the applied physical schema and, on success,
/// publish that target's session source. Implementations are provider-specific and must be idempotent, since
/// both the shell lifecycle and the plain hosted-service lifecycle drive admission.
/// </summary>
/// <remarks>
/// Provider initializers implement this rather than <c>IShellInitializer</c> directly so that a host can admit
/// several targets of the same provider. A per-provider initializer type registered once per target would
/// register the same CLR type twice, which the shell initializer registration keys on.
/// </remarks>
public interface IGroundworkTargetAdmission
{
    /// <summary>The canonical name of the target this admission publishes.</summary>
    string TargetName { get; }

    /// <summary>Admits the target's applied schema and publishes its session source. Idempotent.</summary>
    Task AdmitAsync(CancellationToken cancellationToken = default);
}
