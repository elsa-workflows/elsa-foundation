using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core;
using Elsa.Expressions.Models;
using Elsa.Primitives.Extensions;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Expressions.Services
{
    /// <inheritdoc />
    public sealed class VariableMapper(IWellKnownTypeRegistry wellKnownTypeRegistry, IObjectConverter objectConverter, IVariableFormatter variableFormatter, ILogger<VariableMapper> logger) : IVariableMapper
    {
        /// <inheritdoc />
        public IVariable Map(IVariableDefinition source)
        {
            var typeName = source.TypeName;

            if (string.IsNullOrWhiteSpace(source.TypeName))
                typeName = wellKnownTypeRegistry.GetAliasOrDefault(typeof(object));

            if (!wellKnownTypeRegistry.TryGetTypeOrDefault(typeName, out var type))
                type = typeof(object);

            var variableGenericType = typeof(Variable<>).MakeGenericType(type);
            var variable = (Variable)Activator.CreateInstance(variableGenericType)!;

            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            variable.Id = source.Id ?? Guid.NewGuid().ToString("N"); // Temporarily assign a new ID if the source doesn't have one.
            variable.Name = source.Name;

            objectConverter.TryConvertTo(source.Value, type)
                .OnSuccess(value => variable.Value = value)
                .OnFailure(e => logger.LogWarning("Failed to convert {SourceValue} to {TargetType}", source.Value, type.Name));

            variable.StorageDriverType = !string.IsNullOrEmpty(source.StorageDriverTypeName)
                ? Type.GetType(source.StorageDriverTypeName)
                : null;

            return variable;
        }

        /// <inheritdoc />
        public IVariableDefinition Map(IVariable source)
        {
            var variableType = source.GetType();
            var valueType = variableType.IsConstructedGenericType ? variableType.GetGenericArguments().FirstOrDefault() ?? typeof(object) : typeof(object);
            var valueTypeAlias = wellKnownTypeRegistry.GetAliasOrDefault(valueType);
            var storageDriverTypeName = source.StorageDriverType?.GetSimpleAssemblyQualifiedName();
            var formattedValue = variableFormatter.Format(source);

            return new VariableDefinition(source.Id, source.Name, valueTypeAlias, formattedValue, storageDriverTypeName);
        }
    }
}
