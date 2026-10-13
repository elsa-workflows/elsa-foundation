namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Optional designer metadata for a CLR activity type. Reconciliation copies a valid
/// <see cref="Icon"/> onto an <c>elsa.activity-icon</c> design facet so the authoring catalog can
/// expose it as <c>iconName</c>.
/// </summary>
/// <remarks>
/// Accepted <see cref="Icon"/> values:
/// <list type="bullet">
/// <item>A lucide export or kebab name (<c>Rocket</c>, <c>git-branch</c>).</item>
/// <item><c>asset:&lt;package&gt;/&lt;path&gt;</c> — a static web asset in the extension package.</item>
/// <item><c>ext:&lt;name&gt;</c> — a React icon registered on the Studio client via <c>registerActivityIcons</c>.</item>
/// </list>
/// An unknown, empty or unsafe value is ignored so the designer keeps its type/category fallback.
/// <see cref="Icon"/> is presentation only: the <c>elsa.activity-icon</c> facet is excluded from the
/// descriptor hash, so adding or changing it does not throw <c>ActivityVersionHashMismatchException</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ActivityAttribute : Attribute
{
    /// <summary>The designer icon name. See the type remarks for the accepted forms.</summary>
    public string? Icon { get; init; }
}
