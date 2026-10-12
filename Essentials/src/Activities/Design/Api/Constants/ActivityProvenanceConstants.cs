namespace Elsa.Activities.Design.Api.Constants;

internal static class ActivityProvenanceConstants
{
    /// <summary>
    /// The stable Runtime consumer key for CLR-backed activity versions. Deliberately duplicated from
    /// <c>Elsa.Activities.Runtime.Core</c>'s <c>WellKnownRuntimeActivityConsumers.ClrActivity</c>:
    /// duplication beats a project dependency for a single frozen wire-contract string.
    /// </summary>
    internal const string ClrActivityConsumerKey = "elsa.clr-activity";
}
