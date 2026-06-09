using System.Reflection;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

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

            // FIX: assignability check on the property's declared type, not reference inequality.
            if (!property.PropertyType.IsInstanceOfType(argument))
                throw new InvalidOperationException(
                    $"Argument for property '{name}' is '{argument.GetType().Name}', not assignable to '{property.PropertyType.Name}'.");

            var setter = property.GetSetMethod()
                ?? throw new InvalidOperationException($"Activity property '{name}' has no public setter.");

            setter.Invoke(activity, [argument]);
        }
    }
}
