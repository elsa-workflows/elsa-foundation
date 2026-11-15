using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>Previews publication's conversion decision for a pair of admitted value contracts.</summary>
public interface IActivityBindingConversionPreviewer
{
    ActivityBindingConversionPreview Preview(TypeReference sourceType, ValueRepresentation representation,
        TypeReference targetType, AuthoredValueConversionRequest? conversion);
}
