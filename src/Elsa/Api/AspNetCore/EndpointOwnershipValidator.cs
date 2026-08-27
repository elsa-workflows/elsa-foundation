using Microsoft.AspNetCore.Builder;

namespace Elsa.Api.AspNetCore;

/// <summary>Why an endpoint's ownership metadata was rejected.</summary>
public enum EndpointOwnershipViolationCategory
{
    /// <summary>The endpoint carries no <see cref="EndpointOwnershipMetadata"/>.</summary>
    MissingOwnership,

    /// <summary>The endpoint carries more than one, so no single module owns it.</summary>
    DuplicateOwnership
}

/// <summary>Thrown before endpoint publication when an endpoint is not owned by exactly one module.</summary>
public sealed class UnownedEndpointException(EndpointOwnershipViolationCategory category, string endpoint, string detail)
    : InvalidOperationException($"Endpoint ownership validation failed: endpoint='{endpoint}'; category={category}; {detail}")
{
    public EndpointOwnershipViolationCategory Category { get; } = category;
    public string Endpoint { get; } = endpoint;
}

/// <summary>
/// Enforces the one invariant Elsa's endpoint inventory rests on: every endpoint is owned by exactly
/// one module or host.
/// </summary>
/// <remarks>
/// This used to live inside the unload-safety validator, which now ships in NativeEndpoints and has
/// no reason to know what an Elsa owner is. Ownership is Elsa's vocabulary, so its invariant is
/// Elsa's to enforce, and enforcing it at mapping time is what stops an unowned endpoint reaching
/// the manifest — where the failure is a test report rather than a startup error, long after the
/// mapping that caused it.
/// </remarks>
public static class EndpointOwnershipValidator
{
    /// <summary>Validates that the completed metadata names exactly one owner.</summary>
    public static EndpointOwnershipMetadata Validate(EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoint = builder.DisplayName ?? "<unnamed>";
        var ownership = builder.Metadata.OfType<EndpointOwnershipMetadata>().ToArray();
        if (ownership.Length == 0)
        {
            throw new UnownedEndpointException(
                EndpointOwnershipViolationCategory.MissingOwnership,
                endpoint,
                $"no {nameof(EndpointOwnershipMetadata)} is present");
        }

        if (ownership.Length > 1)
        {
            // Named rather than counted: a duplicate is nearly always two modules mapping the same
            // route, and the owner ids are what identify which two.
            var owners = string.Join(", ", ownership.Select(x => x.Owner).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            throw new UnownedEndpointException(
                EndpointOwnershipViolationCategory.DuplicateOwnership,
                endpoint,
                $"owners={owners}");
        }

        return ownership[0];
    }
}
