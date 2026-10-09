namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Declares <see cref="IRuntimeSecretMask"/> as a single-implementation replacement contract (framework constitution
/// §2.6.2).
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class RuntimeSecretMaskReplacementContractAttribute : Attribute;

/// <summary>
/// Holds the values resolved from secrets for the activity executions handled in one dependency scope, and replaces
/// them in text produced for the execution that resolved them (spec 188, FR-012). The activities runtime's work
/// handlers mask with it the message, stack trace and inner exception chain of the exception a fault boundary records,
/// the message of an <c>ActivityFault</c> the activity returned, and the disposal failures a cancellation arm reports
/// with the cancellation, so those reach the execution's fault, incident and <c>runtime.fault*</c> metadata masked.
/// Not masked: fault codes, a returned fault's category and fault type, exception type names, and what the activity
/// writes to the console or its own logger, returns as outputs, or keeps in private state or bookmarks.
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
/// Every non-empty value is masked however short; a one-character value makes the text hard to read, while a minimum
/// length would let a short value through in the forms the mask matches.
/// </description></item>
/// <item><description>
/// A value is matched ordinally, as written and in its JSON-escaped forms, and replaced by the marker
/// <c>[secret:&lt;reference name&gt;]</c>. A marker that was inserted is never matched again.
/// </description></item>
/// <item><description>
/// Values are held in memory only, until <see cref="Release"/> or the end of the dependency scope, whichever comes
/// first. An implementation must not persist, log or serialize them.
/// </description></item>
/// <item><description>
/// <see cref="HasRegistrations"/> answers faithfully: <see langword="true"/> from the first <see cref="Register"/> of a
/// non-empty value for the execution until <see cref="Release"/>. The fault boundaries mask only while it is
/// <see langword="true"/>, so an implementation that answers <see langword="false"/> while it holds a value disables
/// masking for that execution: the original exception and returned fault are recorded unmasked (fail-open).
/// </description></item>
/// <item><description>
/// <see cref="Release"/> forgets the execution's values. The work handlers call it once they have recorded the outcome,
/// which bounds how long a value is held by the outcome rather than by the scope's lifetime. An implementation that
/// ignores it keeps the values until the scope ends; nothing is masked less.
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

    /// <summary>
    /// Whether any value is registered for <paramref name="activityExecutionId"/>. The fault boundaries replace an
    /// exception only while this is <see langword="true"/>; a <see langword="false"/> answer while a value is held turns
    /// masking off for the execution.
    /// </summary>
    bool HasRegistrations(string activityExecutionId);

    /// <summary>
    /// Returns <paramref name="text"/> with every value registered for <paramref name="activityExecutionId"/> replaced
    /// by its marker, or <paramref name="text"/> itself when none occurs in it.
    /// </summary>
    string Mask(string activityExecutionId, string text);

    /// <summary>Forgets every value registered for <paramref name="activityExecutionId"/>.</summary>
    void Release(string activityExecutionId);
}
