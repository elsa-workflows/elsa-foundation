using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

internal sealed class ActivityBindingConversionPreviewer(ValueConversionPlanResolver resolver) : IActivityBindingConversionPreviewer
{
    public ActivityBindingConversionPreview Preview(TypeReference sourceType, ValueRepresentation representation,
        TypeReference targetType, AuthoredValueConversionRequest? conversion)
    {
        var canSerializeJsonString = CanSerializeJsonString(sourceType, representation, targetType);
        try
        {
            var plan = resolver.Resolve(new(sourceType.Alias, sourceType.CollectionKind), representation,
                new(targetType.Alias, targetType.CollectionKind), AuthoredValueConversionMapper.Mode(conversion),
                AuthoredValueConversionMapper.Profile(conversion), AuthoredValueConversionMapper.Limits(conversion),
                AuthoredValueConversionMapper.Options(conversion));
            return new(sourceType, targetType, plan.Operation.ToString(), CanSerializeJsonString: canSerializeJsonString);
        }
        catch (ValueConversionPublicationException error)
        {
            return new(sourceType, targetType, null, error.ReasonCode.ToString(), error.Reason, canSerializeJsonString);
        }
        catch (ArgumentException)
        {
            // A profile, limits or options may be incomplete while the author is editing them.
            return new(sourceType, targetType, null, "InvalidConversionSettings",
                "The conversion settings are invalid. Check the profile, limits and options.", canSerializeJsonString);
        }
    }

    private bool CanSerializeJsonString(TypeReference sourceType, ValueRepresentation representation, TypeReference targetType)
    {
        try
        {
            resolver.Resolve(new(sourceType.Alias, sourceType.CollectionKind), representation,
                new(targetType.Alias, targetType.CollectionKind), ValueConversionMode.JsonString);
            return true;
        }
        catch (ValueConversionPublicationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
