using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Publishing's <see cref="IActivitySideEffectProfileReader"/> (spec 261 FR-513): resolves the activity version's
/// descriptor as <see cref="ExecutableNodeCompiler"/> does and applies the same attribute rule. A CLR activity gets its
/// declared profile; any other consumer, such as a reusable (graph) activity, compiles without a CLR contract and is
/// treated as <see cref="SideEffectProfile.External"/>.
/// </summary>
public sealed class ActivitySideEffectProfileReader(
    IActivityDefinitionVersionStore activityVersions,
    IWellKnownTypeRegistry wellKnownTypeRegistry) : IActivitySideEffectProfileReader
{
    public async ValueTask<SideEffectProfile?> ReadAsync(string activityVersionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(activityVersionId))
            return null;

        Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersion version;
        try
        {
            version = await activityVersions.GetAsync(activityVersionId, cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            return null;
        }

        if (!StringComparer.Ordinal.Equals(version.ConsumerKey, WellKnownRuntimeActivityConsumers.ClrActivity))
            return SideEffectProfile.External;

        var descriptor = new RuntimeActivityDescriptor(version.ConsumerKey, version.ConsumerSchemaVersion, version.DescriptorPayload);
        return ClrActivityTypeResolver.Resolve(wellKnownTypeRegistry, descriptor) is { } activityType
            ? ActivitySideEffectProfiles.Of(activityType)
            : null;
    }
}
