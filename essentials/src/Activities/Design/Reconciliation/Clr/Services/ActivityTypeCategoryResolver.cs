using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Primitives.Extensions;
using System.ComponentModel;
using System.Reflection;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// Uses a nonblank <see cref="CategoryAttribute"/> declared directly on the CLR activity. Otherwise
/// the last dot-separated segment of the declaring assembly's simple name groups activities without
/// a per-activity annotation. An assembly named <c>Elsa.Runtime.Activities.Primitives</c> yields <c>Primitives</c>.
/// The raw segment is humanized for display (<c>ControlFlow</c> → <c>Control Flow</c>, <c>Bpmn</c> → <c>BPMN</c>).
/// </summary>
public sealed class ActivityTypeCategoryResolver : IActivityTypeCategoryResolver
{
    public string? Resolve(Type type, Assembly assembly)
    {
        var explicitCategory = type.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType.FullName == typeof(CategoryAttribute).FullName);
        if (explicitCategory?.ConstructorArguments is [{ Value: string category }] && !string.IsNullOrWhiteSpace(category))
            return category;

        var simpleName = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(simpleName))
            return null;

        var segments = simpleName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments is [.., var last] ? last.Humanize() : null;
    }
}
