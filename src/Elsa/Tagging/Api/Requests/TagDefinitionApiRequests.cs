using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Requests;

public sealed class CreateTagDefinitionApiRequest
{
    public string CanonicalKey { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public TagValueMode ValueMode { get; set; } = TagValueMode.Marker;
    public TagCardinality Cardinality { get; set; } = TagCardinality.Single;

    internal CreateTagDefinitionRequest ToCoreRequest() => new()
    {
        CanonicalKey = CanonicalKey,
        DisplayName = DisplayName,
        Description = Description,
        Color = Color,
        ValueMode = ValueMode,
        Cardinality = Cardinality,
        IsHostProvisioning = false
    };
}

public sealed class CreateControlledTagValueApiRequest
{
    public string TagDefinitionId { get; set; } = "";
    public string CanonicalKey { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    internal CreateControlledTagValueRequest ToCoreRequest() => new() { TagDefinitionId = TagDefinitionId, CanonicalKey = CanonicalKey, DisplayName = DisplayName, Description = Description, Color = Color, SortOrder = SortOrder };
}

public sealed class UpdateControlledTagValueApiRequest
{
    private string? _displayName; private string? _description; private string? _color; private TagDefinitionStatus? _status; private int _sortOrder;
    public string TagDefinitionId { get; set; } = "";
    public string ControlledTagValueId { get; set; } = "";
    public string? DisplayName { get => _displayName; set { _displayName = value; HasDisplayName = true; } }
    public string? Description { get => _description; set { _description = value; HasDescription = true; } }
    public string? Color { get => _color; set { _color = value; HasColor = true; } }
    public TagDefinitionStatus? Status { get => _status; set { _status = value; HasStatus = true; } }
    public int SortOrder { get => _sortOrder; set { _sortOrder = value; HasSortOrder = true; } }
    internal bool HasDisplayName { get; private set; }
    internal bool HasDescription { get; private set; }
    internal bool HasColor { get; private set; }
    internal bool HasStatus { get; private set; }
    internal bool HasSortOrder { get; private set; }
    internal UpdateControlledTagValueRequest ToCoreRequest()
    {
        var result = new UpdateControlledTagValueRequest();
        if (HasDisplayName) result.DisplayName = DisplayName; if (HasDescription) result.Description = Description; if (HasColor) result.Color = Color; if (HasStatus) result.Status = Status; if (HasSortOrder) result.SortOrder = SortOrder;
        return result;
    }
}

public sealed class UpdateTagDefinitionApiRequest
{
    private string? _displayName;
    private string? _description;
    private string? _color;
    private TagDefinitionStatus? _status;

    public string TagDefinitionId { get; set; } = "";

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

    internal bool HasDisplayName { get; private set; }
    internal bool HasDescription { get; private set; }
    internal bool HasColor { get; private set; }
    internal bool HasStatus { get; private set; }

    internal UpdateTagDefinitionRequest ToCoreRequest()
    {
        var request = new UpdateTagDefinitionRequest();
        if (HasDisplayName) request.DisplayName = DisplayName;
        if (HasDescription) request.Description = Description;
        if (HasColor) request.Color = Color;
        if (HasStatus) request.Status = Status;
        return request;
    }
}
