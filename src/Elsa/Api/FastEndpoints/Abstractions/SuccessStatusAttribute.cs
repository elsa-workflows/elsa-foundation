namespace Elsa.Api.FastEndpoints.Abstractions;

/// <summary>
/// Declares the HTTP status(es) an endpoint returns on success, so they can be published as contract.
/// </summary>
/// <remarks>
/// An endpoint's success status is written inline in <c>HandleAsync</c> (as the argument to
/// <c>SendAsync</c>/<c>ResponseAsync</c>), which no build-time projection can read. That is why consumers
/// could not learn that <c>POST publishing/workflows/{versionId}/publish</c> answers <c>201 Created</c>:
/// every benchmarked session asserted <c>200</c> and failed.
/// <para>
/// Several endpoints choose between statuses at runtime — publish answers 201 when it creates a
/// publication and 200 when it updates one in place — so this accepts a set plus a
/// <see cref="Condition"/> describing the choice. Declaring a single status where the endpoint really has
/// two would replace one confidently-wrong fact with another.
/// </para>
/// <para>
/// Deliberately opt-in: the published API surface omits the status for an endpoint that does not declare
/// one, rather than defaulting to 200. A guessed default is exactly the kind of confidently-wrong fact
/// that costs a consumer more than publishing nothing at all.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SuccessStatusAttribute(params int[] statusCodes) : Attribute
{
    public int[] StatusCodes { get; } = statusCodes;

    /// <summary>When the endpoint can answer more than one status, states which is returned when.</summary>
    public string? Condition { get; init; }
}
