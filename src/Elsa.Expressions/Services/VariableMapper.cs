using Elsa.Expressions.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Expressions.Services
{
    /// <inheritdoc />
    public sealed class VariableMapper(IWellKnownTypeRegistry wellKnownTypeRegistry, IObjectConverter objectConverter, IVariableDefaultValueFormatter variableFormatter, ILogger<VariableMapper> logger) : IVariableMapper
    {
        /// <inheritdoc />
        public IVariable Map(VariableDefinition source)
        {
            var typeInfo = source.TypeInformation;
            var typeName = typeInfo.GetTypeFullName();

            if (string.IsNullOrWhiteSpace(typeName))
                typeName = wellKnownTypeRegistry.GetAliasOrDefault(typeof(object));

            if (!wellKnownTypeRegistry.TryGetTypeOrDefault(typeName, out var type))
                type = typeof(object);

            var variableGenericType = typeof(Variable<>).MakeGenericType(type);
            var variable = (Variable)Activator.CreateInstance(variableGenericType)!;

            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            variable.Id = source.ReferenceKey ?? Guid.NewGuid().ToString("N"); // Temporarily assign a new ID if the source doesn't have one.
            variable.Name = source.Name;

            var value = source.Default?.Value;
            objectConverter
                .TryConvertTo(value, type)
                .OnSuccess(value => variable.DefaultValue = value)
                .OnFailure(e => logger.LogWarning("Failed to convert {SourceValue} to {TargetType}", value, type.Name));

            var storageDriverTypeName = source.StorageDriverType?.GetTypeFullName();
            variable.StorageDriverType = !string.IsNullOrWhiteSpace(storageDriverTypeName)
                ? Type.GetType(storageDriverTypeName)
                : null;

            return variable;
        }

        /// <inheritdoc />
        public VariableDefinition Map(IVariable source)
        {
            var variableType = source.GetType();
            var valueType = variableType.IsConstructedGenericType ? variableType.GetGenericArguments().FirstOrDefault() ?? typeof(object) : typeof(object);
            var valueTypeInformation = TypeInformation.FromType(valueType);
            var storageDriverType = source.StorageDriverType is not null ? TypeInformation.FromType(source.StorageDriverType) : null;
            var value = variableFormatter.Format(source);
            return new VariableDefinition(source.Id, source.Name, valueTypeInformation, storageDriverType, value);
        }
    }
}
