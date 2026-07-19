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
    public string TagDefinitionId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public TagDefinitionStatus? Status { get; set; }

    internal UpdateTagDefinitionRequest ToCoreRequest() => new()
    {
        DisplayName = DisplayName,
        Description = Description,
        Color = Color,
        Status = Status
    };
}
