using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Entities;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

/// <summary>
/// A definition of an activity type. The catalog's visual shell — identity
/// (<see cref="ActivityTypeKey"/>) plus the display fields (category, display name,
/// description) the picker shows. Source provenance lives on
/// <c>ActivityDefinitionVersion</c>; if you want to know where a Definition came from,
/// look at the first version's <c>SourceKind</c>/<c>SourceId</c>. Implementation details
/// (CLR type info, workflow id, …) live on the version's
/// <c>IImplementationDescriptor</c> — the Definition itself is implementation-agnostic.
/// </summary>
public sealed class ActivityDefinition : TenantEntity, IActivityDefinition
{
    /// <summary>
    /// Stable logical identity. Write-once — immutability enforced via
    /// <c>PropertySaveBehavior.Throw</c> in the EF Core entity configuration.
    /// </summary>
    public string ActivityTypeKey { get; init; } = null!;

    /// <summary>
    /// The category of the activity type.
    /// </summary>
    public string Category { get; set; } = null!;

    /// <summary>
    /// The display name of the activity type.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The description of the activity type.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Builds the persistence entity from any <see cref="IActivityDefinition"/> (e.g. a factory read-model).</summary>
    public static ActivityDefinition From(IActivityDefinition source) =>
        new()
        {
            Id = source.Id,
            ActivityTypeKey = source.ActivityTypeKey,
            Category = source.Category,
            DisplayName = source.DisplayName,
            Description = source.Description,
        };
}