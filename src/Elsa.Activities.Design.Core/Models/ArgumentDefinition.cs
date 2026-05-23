using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models
{
    /// <summary>
    /// 
    /// </summary>
    public record ArgumentDefinition(
        string ReferenceKey,
        string Name,
        TypeInformation TypeInfo,
        string DisplayName,
        string? Category,
        bool? IsBrowsable = null,
        bool? IsSerializable = null,
        string? Description = null,
        float Order = 0,
        string? UiHint = null,
        IDictionary<string, object>? PropertyInfo = null,
        IDictionary<string, object>? UISpecifications = null)

        : IArgumentDefinition
    {
        bool IArgumentDefinition.IsBrowsable => IsBrowsable ?? true;

        bool IArgumentDefinition.IsSerializable => IsSerializable ?? true;
    }
}