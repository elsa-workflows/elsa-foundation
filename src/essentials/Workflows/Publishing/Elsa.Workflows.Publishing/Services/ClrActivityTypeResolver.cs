using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Maps a CLR activity descriptor to the activity's registered CLR type, so publication can read the type's
/// declarations (resume targets, value outcomes, secret binding refusals). Any other consumer, or an alias the
/// registry does not hold, yields <c>null</c>.
/// </summary>
internal static class ClrActivityTypeResolver
{
    public static Type? Resolve(IWellKnownTypeRegistry wellKnownTypeRegistry, RuntimeActivityDescriptor descriptor)
    {
        if (!StringComparer.Ordinal.Equals(descriptor.ConsumerKey, WellKnownRuntimeActivityConsumers.ClrActivity))
            return null;

        var clrDescriptor = descriptor.Payload.Deserialize<ClrActivityDescriptor>(DescriptorPayloadSerializer.Options);
        return clrDescriptor is not null &&
               wellKnownTypeRegistry.TryGetTypeOrDefault(clrDescriptor.TypeAlias, out var activityType) &&
               activityType != typeof(object)
            ? activityType
            : null;
    }
}
