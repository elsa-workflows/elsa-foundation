using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Constructs new <see cref="IActivityDefinitionVersion"/> instances from caller-supplied values,
/// owning the cross-cutting concerns: identity generation and the content <see cref="IActivityDefinitionVersion.Hash"/>
/// (computed via <see cref="IActivityDefinitionHasher"/>). Any domain (reconciliation sources, the
/// design API, Elsa-3 import) builds a version by passing values — no per-source interface impl, no
/// object-mapper. The persistence layer turns the result into its concrete entity via the entity's
/// <c>From</c> method.
/// </summary>
public interface IActivityDefinitionVersionFactory
{
    /// <param name="id">Stable id; when null/blank a fresh one is generated.</param>
    IActivityDefinitionVersion Create(
        IActivityDefinition definition,
        string version,
        string providerKey,
        string providerSchemaVersion,
        string consumerKey,
        string consumerSchemaVersion,
        JsonElement descriptorPayload,
        string sourceKind,
        string sourceId,
        IEnumerable<InputDefinition> inputs,
        IEnumerable<OutputDefinition> outputs,
        IEnumerable<ActivityDesignFacet> designFacets,
        ActivityExecutionType executionType = ActivityExecutionType.Action,
        string? id = null);
}
