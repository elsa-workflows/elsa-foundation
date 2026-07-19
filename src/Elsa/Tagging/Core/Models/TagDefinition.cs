using System.Text.Json.Serialization;
using Elsa.Primitives.Identity;

namespace Elsa.Tagging.Core.Models;

/// <summary>A tenant-scoped, reusable marker vocabulary item. Workflow Design owns assignments to this catalog item.</summary>
public sealed class TagDefinition
{
    public string Id { get; set; } = ShortIdentityGenerator.Generate(DateTimeOffset.UtcNow);
    public string CanonicalKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? Color { get; set; }
    public TagDefinitionStatus Status { get; set; } = TagDefinitionStatus.Active;
    public TagDefinitionEligibility Eligibility { get; set; } = TagDefinitionEligibility.WorkflowDefinition;
    /// <summary>Defaults preserve the marker catalog contract introduced before controlled values.</summary>
    public TagValueMode ValueMode { get; set; } = TagValueMode.Marker;
    public TagCardinality Cardinality { get; set; } = TagCardinality.Single;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TagValueMode
{
    Marker,
    Controlled,
    FreeText
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TagCardinality
{
    Single,
    Multiple
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TagDefinitionStatus
{
    Active,
    Retired
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TagDefinitionEligibility
{
    None = 0,
    WorkflowDefinition = 1
}

public static class TagDefinitionConstraints
{
    public const int MaximumCanonicalKeyLength = 64;
    public const int MaximumDisplayNameLength = 128;
    public const int MaximumDescriptionLength = 1024;
    public const int MaximumColorLength = 32;
    public const string ReservedNamespacePrefix = "elsa.";

    public static void ValidateValueSemantics(TagValueMode valueMode, TagCardinality cardinality)
    {
        if (valueMode == TagValueMode.Marker && cardinality != TagCardinality.Single)
            throw new ArgumentException("Marker tags are always single-valued.", nameof(cardinality));
        if (valueMode == TagValueMode.Controlled && cardinality != TagCardinality.Single)
            throw new ArgumentException("Controlled tags are single-valued in this release.", nameof(cardinality));
        if (valueMode == TagValueMode.FreeText)
            throw new ArgumentException("Free-text tags are not supported in this release.", nameof(valueMode));
    }

    public static void ValidateCanonicalKey(string value, bool isHostProvisioning)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumCanonicalKeyLength)
            throw new ArgumentException($"A tag canonical key must contain 1 to {MaximumCanonicalKeyLength} characters.", nameof(value));

        if (!char.IsAsciiLetterLower(value[0]) || value.Any(character => !IsCanonicalCharacter(character)))
            throw new ArgumentException("A tag canonical key must be lowercase ASCII and start with a letter; only letters, digits, '.', '_' and '-' are allowed.", nameof(value));

        if (value.StartsWith(ReservedNamespacePrefix, StringComparison.Ordinal) && !isHostProvisioning)
            throw new ArgumentException($"The '{ReservedNamespacePrefix}' namespace is reserved for authorized host provisioning.", nameof(value));
    }

    public static void ValidateMutableFields(string displayName, string? description, string? color)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaximumDisplayNameLength)
            throw new ArgumentException($"A tag display name must contain 1 to {MaximumDisplayNameLength} characters.", nameof(displayName));
        if (description?.Length > MaximumDescriptionLength)
            throw new ArgumentException($"A tag description cannot exceed {MaximumDescriptionLength} characters.", nameof(description));
        if (color?.Length > MaximumColorLength)
            throw new ArgumentException($"A tag color cannot exceed {MaximumColorLength} characters.", nameof(color));
    }

    private static bool IsCanonicalCharacter(char character) =>
        char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_' or '-';
}

public sealed class CreateTagDefinitionRequest
{
    public string CanonicalKey { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public bool IsHostProvisioning { get; set; }
    public TagValueMode ValueMode { get; set; } = TagValueMode.Marker;
    public TagCardinality Cardinality { get; set; } = TagCardinality.Single;
}

public sealed class UpdateTagDefinitionRequest
{
    private string? _displayName;
    private string? _description;
    private string? _color;
    private TagDefinitionStatus? _status;

    public string? DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            HasDisplayName = true;
        }
    }

    public string? Description
    {
        get => _description;
        set
        {
            _description = value;
            HasDescription = true;
        }
    }

    public string? Color
    {
        get => _color;
        set
        {
            _color = value;
            HasColor = true;
        }
    }

    public TagDefinitionStatus? Status
    {
        get => _status;
        set
        {
            _status = value;
            HasStatus = true;
        }
    }

    public bool HasDisplayName { get; private set; }
    public bool HasDescription { get; private set; }
    public bool HasColor { get; private set; }
    public bool HasStatus { get; private set; }
}

public sealed class TagDefinitionListRequest
{
    public bool ActiveOnly { get; set; } = true;
}

/// <summary>A stable controlled vocabulary value owned by one controlled tag definition.</summary>
public sealed class ControlledTagValue
{
    public string Id { get; set; } = ShortIdentityGenerator.Generate(DateTimeOffset.UtcNow);
    public string TagDefinitionId { get; set; } = "";
    public string CanonicalKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? Color { get; set; }
    public TagDefinitionStatus Status { get; set; } = TagDefinitionStatus.Active;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public static class ControlledTagValueConstraints
{
    public const int MaximumCanonicalKeyLength = TagDefinitionConstraints.MaximumCanonicalKeyLength;
    public const int MaximumValuesPerDefinition = 100;

    public static void Validate(string tagDefinitionId, string canonicalKey, string displayName, string? description, string? color, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagDefinitionId);
        TagDefinitionConstraints.ValidateCanonicalKey(canonicalKey, isHostProvisioning: true);
        TagDefinitionConstraints.ValidateMutableFields(displayName, description, color);
        if (sortOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "A controlled value sort order cannot be negative.");
    }

    public static string DisplayNameSortKey(string displayName) =>
        displayName.Normalize().ToUpperInvariant();
}

public sealed class CreateControlledTagValueRequest
{
    public string TagDefinitionId { get; set; } = "";
    public string CanonicalKey { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public int SortOrder { get; set; }
}

public sealed class UpdateControlledTagValueRequest
{
    private string? _displayName;
    private string? _description;
    private string? _color;
    private TagDefinitionStatus? _status;
    private int _sortOrder;

    public string? DisplayName { get => _displayName; set { _displayName = value; HasDisplayName = true; } }
    public string? Description { get => _description; set { _description = value; HasDescription = true; } }
    public string? Color { get => _color; set { _color = value; HasColor = true; } }
    public TagDefinitionStatus? Status { get => _status; set { _status = value; HasStatus = true; } }
    public int SortOrder { get => _sortOrder; set { _sortOrder = value; HasSortOrder = true; } }
    public bool HasDisplayName { get; private set; }
    public bool HasDescription { get; private set; }
    public bool HasColor { get; private set; }
    public bool HasStatus { get; private set; }
    public bool HasSortOrder { get; private set; }
}

public sealed class ControlledTagValueListRequest
{
    public string TagDefinitionId { get; set; } = "";
    public bool ActiveOnly { get; set; } = true;
}
