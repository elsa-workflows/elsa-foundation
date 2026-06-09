namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Constructs new <see cref="IActivityDefinition"/> instances from the caller-supplied values,
/// owning the cross-cutting concerns (identity generation). Any domain can build a definition by
/// passing values — no per-source model or object-mapper. Reads never use this: a persisted entity
/// already implements the read contract. The persistence layer turns the result into its concrete
/// entity via the entity's <c>From</c> method.
/// </summary>
public interface IActivityDefinitionFactory
{
    /// <param name="id">Stable id; when null/blank a fresh one is generated.</param>
    IActivityDefinition Create(
        string activityTypeKey,
        string category,
        string? displayName = null,
        string? description = null,
        string? id = null);
}
