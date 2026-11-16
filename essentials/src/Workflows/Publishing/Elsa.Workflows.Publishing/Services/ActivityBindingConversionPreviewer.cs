using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

internal sealed class ActivityBindingConversionPreviewer(ValueConversionPlanResolver resolver) : IActivityBindingConversionPreviewer
{
    public ActivityBindingConversionPreview Preview(TypeReference sourceType, ValueRepresentation representation,
        TypeReference targetType, AuthoredValueConversionRequest? conversion)
    {
        try
        {
            var plan = resolver.Resolve(new(sourceType.Alias, sourceType.CollectionKind), representation,
                new(targetType.Alias, targetType.CollectionKind), AuthoredValueConversionMapper.Mode(conversion),
                AuthoredValueConversionMapper.Profile(conversion), AuthoredValueConversionMapper.Limits(conversion),
                AuthoredValueConversionMapper.Options(conversion));
            return new(sourceType, targetType, plan.Operation.ToString());
        }
        catch (ValueConversionPublicationException error)
        {
            return new(sourceType, targetType, null, error.ReasonCode.ToString(), error.Reason);
        }
        catch (ArgumentException)
        {
            // A profile, limits or options may be incomplete while the author is editing them.
            return new(sourceType, targetType, null, "InvalidConversionSettings",
                "The conversion settings are invalid. Check the profile, limits and options.");
        }
    }
}
