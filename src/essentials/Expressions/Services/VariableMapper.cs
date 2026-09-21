using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Primitives.Identity;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Expressions.Services;

/// <inheritdoc />
public sealed class VariableMapper(IWellKnownTypeRegistry wellKnownTypeRegistry, IObjectConverter objectConverter, IVariableDefaultValueFormatter variableFormatter, ILogger<VariableMapper> logger) : IVariableMapper
{
    /// <inheritdoc />
    public IVariable Map(VariableDefinition source)
    {
        var elementType = ResolveAlias(source.Type.Alias);
        var closedType = TypeReferenceFactory.Close(elementType, source.Type.CollectionKind);

        var variableGenericType = typeof(Variable<>).MakeGenericType(closedType);
        var variable = (Variable)Activator.CreateInstance(variableGenericType)!;

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        variable.Id = source.ReferenceKey ?? ShortIdentityGenerator.Generate(DateTimeOffset.UtcNow); // Temporarily assign a new ID if the source doesn't have one.
        variable.Name = source.Name;

        var value = source.Default?.Value;
        objectConverter
            .TryConvertTo(value, closedType)
            .OnSuccess(value => variable.DefaultValue = value)
            .OnFailure(e => logger.LogWarning("Failed to convert {SourceValue} to {TargetType}", value, closedType.Name));

        variable.StorageDriverType = !string.IsNullOrWhiteSpace(source.StorageDriverType)
            && wellKnownTypeRegistry.TryGetTypeOrDefault(source.StorageDriverType, out var storageDriverType)
            ? storageDriverType
            : null;

        return variable;
    }

    /// <inheritdoc />
    public VariableDefinition Map(IVariable source)
    {
        var variableType = source.GetType();
        var valueType = variableType.IsConstructedGenericType ? variableType.GetGenericArguments().FirstOrDefault() ?? typeof(object) : typeof(object);
        var typeReference = TypeReferenceFactory.FromClrType(valueType, wellKnownTypeRegistry.GetAliasOrDefault);
        var storageDriverType = source.StorageDriverType is not null ? wellKnownTypeRegistry.GetAliasOrDefault(source.StorageDriverType) : null;
        var value = variableFormatter.Format(source);
        return new VariableDefinition(source.Id, source.Name, typeReference, storageDriverType, value);
    }

    private Type ResolveAlias(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return typeof(object);

        if (wellKnownTypeRegistry.TryGetTypeOrDefault(alias, out var type))
            return type;

        logger.LogWarning("Could not resolve unknown type alias '{Alias}'; falling back to object.", alias);
        return typeof(object);
    }
}
