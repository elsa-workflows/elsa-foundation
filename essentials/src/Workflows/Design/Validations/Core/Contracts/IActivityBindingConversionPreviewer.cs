using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>Identifies a shell's single authority for authoring-time conversion previews.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class BindingConversionPreviewReplacementContractAttribute : Attribute;

/// <summary>Replacement contract for publication's conversion decision on an admitted pair of value contracts.</summary>
[BindingConversionPreviewReplacementContract]
public interface IActivityBindingConversionPreviewer
{
    ActivityBindingConversionPreview Preview(TypeReference sourceType, ValueRepresentation representation,
        TypeReference targetType, AuthoredValueConversionRequest? conversion);
}
