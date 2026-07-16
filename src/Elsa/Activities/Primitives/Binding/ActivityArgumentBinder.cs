using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
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
            var property = argumentProperties.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                // During the value-flow migration, plain [ActivityInput] properties are hydrated once by
                // ActivityInputHydrator after construction. Do not force those values back through the
                // obsolete InputArgument channel merely because a legacy executable descriptor was loaded.
                if (argumentBaseType == typeof(InputArgument) && HasPlainInput(activity.GetType(), name))
                    continue;

                throw new InvalidOperationException(
                    $"Activity '{activity.GetType().Name}' has no {argumentBaseType.Name} property named '{name}'.");
            }

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

    private static bool HasPlainInput(Type activityType, string name) =>
        activityType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property =>
        {
            var attribute = property.GetCustomAttribute<ActivityInputAttribute>(inherit: true);
            return attribute is not null &&
                   (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    (attribute.Key ?? property.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
        });

    /// <summary>
    /// Returns <paramref name="argument"/> unchanged when it is already assignable to <paramref name="propertyType"/>.
    /// Otherwise, when both are closed generics of the <b>same</b> argument family (never cross-wrapping
    /// Input↔Output) and the value types are compatible for that family's data-flow direction, rebuilds the
    /// argument as the property's declared type over the same <see cref="IMemoryBlockReference"/> so the
    /// seeded value continues to resolve. Direction per family (#313/#383):
    /// <list type="bullet">
    /// <item><c>InputArgument&lt;&gt;</c> — covariant: the activity <i>reads</i> the block, so the property's
    /// value type must be assignable from the argument's (<c>InputArgument&lt;int&gt;</c> →
    /// <c>InputArgument&lt;object&gt;</c> property).</item>
    /// <item><c>OutputArgument&lt;&gt;</c> — contravariant: the activity <i>writes</i> the block, so the
    /// argument's capture-slot value type must be assignable from the property's
    /// (<c>OutputArgument&lt;object?&gt;</c> capture → <c>OutputArgument&lt;string&gt;</c> property).</item>
    /// </list>
    /// Throws when the types are genuinely incompatible.
    /// </summary>
    private static object CoerceToPropertyType(Argument argument, Type propertyType, string name)
    {
        if (propertyType.IsInstanceOfType(argument))
            return argument;

        if (TryGetArgumentValueType(propertyType, out var targetFamily, out var targetValueType)
            && TryGetArgumentValueType(argument.GetType(), out var sourceFamily, out var sourceValueType)
            && targetFamily == sourceFamily
            && (targetFamily == typeof(InputArgument<>)
                ? targetValueType.IsAssignableFrom(sourceValueType)
                : sourceValueType.IsAssignableFrom(targetValueType)))
        {
            var rebuilt = (Argument)Activator.CreateInstance(propertyType, argument.MemoryBlockReference())!;
            return rebuilt;
        }

        throw new InvalidOperationException(
            $"Argument for property '{name}' is '{argument.GetType().Name}', not assignable to '{propertyType.Name}'.");
    }

    private static bool TryGetArgumentValueType(Type type, out Type family, out Type valueType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType)
            {
                var definition = current.GetGenericTypeDefinition();
                if (definition == typeof(InputArgument<>) || definition == typeof(OutputArgument<>))
                {
                    family = definition;
                    valueType = current.GetGenericArguments()[0];
                    return true;
                }
            }
        }

        family = null!;
        valueType = null!;
        return false;
    }
}
