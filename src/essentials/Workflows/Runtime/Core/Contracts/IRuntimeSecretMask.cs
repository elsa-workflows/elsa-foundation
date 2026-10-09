namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Declares <see cref="IRuntimeSecretMask"/> as a single-implementation replacement contract (framework constitution
/// §2.6.2).
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class RuntimeSecretMaskReplacementContractAttribute : Attribute;

/// <summary>
/// Holds the values resolved from secrets for the activity executions handled in one dependency scope, and replaces
/// them in text produced for the execution that resolved them, so that a resolved value does not reach that
/// execution's fault, its incident, or a log line that renders the exception the fault boundary hands on (spec 188,
/// FR-012).
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>replacement contract</b> (framework constitution §2.6.2), declared by
/// <see cref="RuntimeSecretMaskReplacementContractAttribute"/>: at most one implementation per container. The runtime
/// registers a scoped default with <c>TryAdd</c>, so a host replaces it with <c>services.Replace(...)</c> or by
/// registering its own first. A host that composes more than one does not start: the activities runtime's startup
/// check fails shell activation with <see cref="Exceptions.MultipleRuntimeSecretMasksException"/>, naming every
/// registration.
/// </para>
/// <para>
/// Every implementation keeps these semantics, which the activities runtime relies on:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Registrations are kept per activity execution id. <see cref="Mask"/> applies only the values registered for the
/// execution it is given, never another execution's, as FR-012 scopes masking to the execution that resolved them.
/// </description></item>
/// <item><description>
/// An empty value is ignored: it cannot be told apart in text, and registering it must not make every text "masked".
/// Every non-empty value is masked however short; a one-character value makes the text hard to read but never lets
/// the value through, which a minimum length would.
/// </description></item>
/// <item><description>
/// A value is matched ordinally, as written and in its JSON-escaped forms, and replaced by the marker
/// <c>[secret:&lt;reference name&gt;]</c>. A marker that was inserted is never matched again.
/// </description></item>
/// <item><description>
/// Values are held in memory only, until <see cref="Release"/> or the end of the dependency scope, whichever comes
/// first. They are never persisted, logged or serialized.
/// </description></item>
/// </list>
/// </remarks>
[RuntimeSecretMaskReplacementContract]
public interface IRuntimeSecretMask
{
    /// <summary>
    /// Registers <paramref name="value"/>, resolved from the secret named <paramref name="referenceName"/> while
    /// <paramref name="activityExecutionId"/> was activated. An empty value is ignored.
    /// </summary>
    void Register(string activityExecutionId, string referenceName, string value);

    /// <summary>Whether any value is registered for <paramref name="activityExecutionId"/>.</summary>
    bool HasRegistrations(string activityExecutionId);

    /// <summary>
    /// Returns <paramref name="text"/> with every value registered for <paramref name="activityExecutionId"/> replaced
    /// by its marker, or <paramref name="text"/> itself when none occurs in it.
    /// </summary>
    string Mask(string activityExecutionId, string text);

    /// <summary>Forgets every value registered for <paramref name="activityExecutionId"/>.</summary>
    void Release(string activityExecutionId);
}
