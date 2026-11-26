using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Contracts;

namespace Elsa.Activities.Design.Persistence.Core.Services;

/// <summary>Default <see cref="IActivityDefinitionFactory"/> — generates the id, returns a read-model.</summary>
public sealed class ActivityDefinitionFactory(IIdentityGenerator identityGenerator) : IActivityDefinitionFactory
{
    public IActivityDefinition Create(
        string activityTypeKey,
        string category,
        string? displayName = null,
        string? description = null,
        string? id = null)
        => new ActivityDefinitionModel(
            Id: string.IsNullOrWhiteSpace(id) ? identityGenerator.Generate() : id,
            ActivityTypeKey: activityTypeKey,
            Category: category,
            DisplayName: displayName,
            Description: description);
}
