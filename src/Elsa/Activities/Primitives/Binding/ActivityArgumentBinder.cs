using System.Reflection;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;

namespace Elsa.Activities.Primitives.Binding;

/// <summary>
/// Binds author-filled named arguments to a CLR activity's typed <c>InputArgument&lt;T&gt;</c> /
/// <c>OutputArgument&lt;T&gt;</c> properties (match by property name, assignable argument type,
/// invoke the public setter). Feature-internal to <c>Elsa.Activities.Primitives</c> — deliberately
/// NOT promoted to a <c>.Core</c> library (a core is for contributor/replacement contracts, not a
/// bucket for feature-internal helpers).
/// </summary>
public sealed class ActivityArgumentBinder
{
    public void Bind(
        IActivity activity,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs)
    {
        if (inputs is not null)
            Assign(activity, inputs.ToDictionary(kv => kv.Key, kv => (object)kv.Value), typeof(InputArgument));

        if (outputs is not null)
            Assign(activity, outputs.ToDictionary(kv => kv.Key, kv => (object)kv.Value), typeof(OutputArgument));
    }

    private static void Assign(IActivity activity, IDictionary<string, object> namedValues, Type argumentBaseType)
    {
        var argumentProperties = activity
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            // FIX: use the property's declared type (not the PropertyInfo's runtime type), and assignability.
            .Where(p => argumentBaseType.IsAssignableFrom(p.PropertyType))
            .ToList();

        foreach (var (name, argument) in namedValues)
        {
            var property = argumentProperties.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Activity '{activity.GetType().Name}' has no {argumentBaseType.Name} property named '{name}'.");

            // The runtime materializer builds the argument as InputArgument<T> from the value's *materialized*
            // CLR type (e.g. InputArgument<int> for a literal Int32). Generic InputArgument<T> is invariant, so
            // such an argument is not assignable to a property declared as a wider type — most notably
            // InputArgument<object> on SetVariable/SetVariables (#313). When the property's generic value type is
            // assignable from the argument's, re-wrap the argument as the property's declared InputArgument<T>
            // over the *same* memory block reference, so the seeded value still resolves under the same id.
            var boundArgument = CoerceToPropertyType((Argument)argument, property.PropertyType, name);

            var setter = property.GetSetMethod()
                ?? throw new InvalidOperationException($"Activity property '{name}' has no public setter.");

            setter.Invoke(activity, [boundArgument]);
        }
    }

    /// <summary>
    /// Returns <paramref name="argument"/> unchanged when it is already assignable to <paramref name="propertyType"/>.
    /// Otherwise, when both are closed <c>InputArgument&lt;&gt;</c> types and the property's value type is assignable
    /// from the argument's (a widening such as <c>InputArgument&lt;int&gt;</c> → <c>InputArgument&lt;object&gt;</c>),
    /// rebuilds the argument as the property's declared type over the same <see cref="IMemoryBlockReference"/> so the
    /// seeded value continues to resolve. Throws when the types are genuinely incompatible.
    /// </summary>
    private static object CoerceToPropertyType(Argument argument, Type propertyType, string name)
    {
        if (propertyType.IsInstanceOfType(argument))
            return argument;

        if (TryGetInputArgumentValueType(propertyType, out var targetValueType)
            && TryGetInputArgumentValueType(argument.GetType(), out var sourceValueType)
            && targetValueType.IsAssignableFrom(sourceValueType))
        {
            var rebuilt = (Argument)Activator.CreateInstance(propertyType, argument.MemoryBlockReference())!;
            return rebuilt;
        }

        throw new InvalidOperationException(
            $"Argument for property '{name}' is '{argument.GetType().Name}', not assignable to '{propertyType.Name}'.");
    }

    private static bool TryGetInputArgumentValueType(Type type, out Type valueType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(InputArgument<>))
            {
                valueType = current.GetGenericArguments()[0];
                return true;
            }
        }

        valueType = null!;
        return false;
    }
}
