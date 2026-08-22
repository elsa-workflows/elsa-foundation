using System.Collections.Immutable;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// Describes the lifetime classification of an endpoint's API Explorer-facing metadata.
/// </summary>
public enum OpenApiLifetimeClassification
{
    HostStatic,
    SharedContract
}

/// <summary>
/// The closed set of metadata categories inspected by the unload-safety boundary.
/// </summary>
public enum OpenApiLifetimeValidationCategory
{
    RequestType,
    ResponseType,
    MetadataObject,
    MemberOrMethod,
    DelegateOrTransformer,
    SerializerMetadata
}

/// <summary>
/// Immutable, value-only marker attached after an endpoint's completed metadata has passed the
/// unload-safe OpenAPI boundary. This marker is intentionally not an endpoint framework abstraction.
/// </summary>
public sealed record OpenApiLifetimeMetadata
{
    public OpenApiLifetimeMetadata(
        string owner,
        OpenApiLifetimeClassification classification,
        string endpoint,
        ImmutableArray<OpenApiLifetimeValidationCategory> checkedCategories)
    {
        if (owner is null)
            throw new ArgumentNullException(nameof(owner));
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("An OpenAPI lifetime owner is required.", nameof(owner));
        if (!Enum.IsDefined(classification))
            throw new ArgumentOutOfRangeException(nameof(classification), classification, "An OpenAPI lifetime classification must be defined.");
        if (endpoint is null)
            throw new ArgumentNullException(nameof(endpoint));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("An OpenAPI lifetime endpoint identity is required.", nameof(endpoint));
        if (checkedCategories.IsDefaultOrEmpty)
            throw new ArgumentException("At least one OpenAPI lifetime validation category is required.", nameof(checkedCategories));

        Owner = owner.Trim();
        Classification = classification;
        Endpoint = endpoint.Trim();
        CheckedCategories = checkedCategories;
    }

    public OpenApiLifetimeMetadata(
        string owner,
        OpenApiLifetimeClassification classification,
        string endpoint)
        : this(owner, classification, endpoint, OpenApiLifetimeValidationCategories.All)
    {
    }

    public string Owner { get; }
    public OpenApiLifetimeClassification Classification { get; }
    public string Endpoint { get; }
    public ImmutableArray<OpenApiLifetimeValidationCategory> CheckedCategories { get; }
}

/// <summary>Provides the fixed validation set recorded by accepted endpoint markers.</summary>
public static class OpenApiLifetimeValidationCategories
{
    public static ImmutableArray<OpenApiLifetimeValidationCategory> All { get; } =
    [
        OpenApiLifetimeValidationCategory.RequestType,
        OpenApiLifetimeValidationCategory.ResponseType,
        OpenApiLifetimeValidationCategory.MetadataObject,
        OpenApiLifetimeValidationCategory.MemberOrMethod,
        OpenApiLifetimeValidationCategory.DelegateOrTransformer,
        OpenApiLifetimeValidationCategory.SerializerMetadata
    ];
}
