using System.Reflection;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// The one rule that gives a CLR activity type its side-effect profile, shared by <see cref="ExecutableNodeCompiler"/>
/// and <see cref="ActivitySideEffectProfileReader"/> so publication and design-time validation cannot drift apart.
/// </summary>
internal static class ActivitySideEffectProfiles
{
    // An unmarked activity is External (fail-safe); only an explicit [ActivitySideEffectProfile(ReplaySafe)] opts a
    // pure, deterministic activity into a deferrable claim boundary.
    public static SideEffectProfile Of(Type activityType) =>
        activityType.GetCustomAttribute<ActivitySideEffectProfileAttribute>(inherit: true)?.Profile
        ?? SideEffectProfile.External;
}
