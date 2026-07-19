using Elsa.Tagging.Core.Models;

namespace Elsa.Tagging.Api.Requests;

public sealed class CreateTagDefinitionApiRequest
{
    public string CanonicalKey { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }

    internal CreateTagDefinitionRequest ToCoreRequest() => new()
    {
        CanonicalKey = CanonicalKey,
        DisplayName = DisplayName,
        Description = Description,
        Color = Color,
        IsHostProvisioning = false
    };
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
