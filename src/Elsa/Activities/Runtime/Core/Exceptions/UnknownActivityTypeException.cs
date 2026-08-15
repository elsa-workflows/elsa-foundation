using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Exceptions;

/// <summary>
/// Thrown at construction time when a CLR activity descriptor's stable type alias does not resolve to a
/// registered CLR type — typically because the activity's assembly is not loaded into this host (so the
/// startup registration pass never registered its alias). A domain failure (Elsa §E2.6), not a system
/// fault: cataloguing and reading the row are unaffected; only construction of an instance fails. The
/// alias is preserved verbatim so the failure names the exact unresolved identity.
/// </summary>
/// <remarks>
/// Derives from <see cref="ActivityResolutionException"/> so <c>ActivityActivationFailureHandler.Classify</c>
/// can recognise it. It previously extended <see cref="Exception"/> directly, which made <c>Classify</c>
/// return null: a missing CLR type was the one activation failure never classified as a deployment
/// incident, so it surfaced as an unclassified fault instead of a non-retryable
/// "correct deployment and resume" incident like every sibling failure. The import gate (FR-B-005a) is
/// the primary detection path; this classification is the defense-in-depth behind it.
/// </remarks>
public sealed class UnknownActivityTypeException(string typeAlias)
    : ActivityResolutionException(
        $"No CLR activity type is registered for the alias '{typeAlias}'. The activity's assembly may not be loaded in this host.",
        WellKnownRuntimeActivityConsumers.ClrActivity,
        // The CLR activation mechanism resolved fine — it is the type behind the alias that is
        // absent. So the schema reported is the one CLR activation advertises support for, matching
        // ClrActivityActivator.SupportedSchemaVersions. ActivityActivationFailure rejects an empty
        // schema version, so a sentinel is not an option here.
        RuntimeActivityDescriptor.InitialSchemaVersion)
{
    public string TypeAlias { get; } = typeAlias;
}
