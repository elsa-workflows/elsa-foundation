namespace Elsa.Activities.Design.Api.Contracts;

/// <summary>
/// Resolves the shell feature that provides a given activity type key, so authoring clients can tell
/// which feature to enable to make an activity usable.
/// </summary>
/// <remarks>
/// Resolution is best-effort and meaningful for CLR-provided catalog rows only: the activity type key is
/// resolved through the well-known type registry to a live CLR type, whose assembly is matched against the
/// shell's runtime feature catalog (activity-bearing assemblies carry exactly one shell feature class).
/// A non-null result may name a feature that is currently disabled in the shell composition — the activity
/// catalog is folder-scanned, so a disabled feature's activities still surface; that is intentional signal
/// for clients ("enable feature X to use this activity"). The resolver returns <c>null</c> whenever
/// attribution cannot be established: unknown alias, the hosting shell lacks a type registry or runtime
/// feature catalog, or no feature startup type lives in the activity type's assembly.
/// </remarks>
public interface IActivityFeatureAttributionResolver
{
    /// <summary>
    /// Returns the shell feature id that provides <paramref name="activityTypeKey"/>, or <c>null</c>
    /// when the attribution cannot be established.
    /// </summary>
    ValueTask<string?> ResolveProvidingFeatureAsync(string activityTypeKey, CancellationToken cancellationToken);
}
