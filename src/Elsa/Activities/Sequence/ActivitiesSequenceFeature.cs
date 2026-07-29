using CShells.Features;
using Elsa.Activities.Sequence.Internal;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Sequence;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesSequence",
    DisplayName = "Activities Sequence",
    Description = "Sequence composite activity and executable-node child slot contracts."
)]
public class ActivitiesSequenceFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IActivityStructureHandler, SequenceStructureHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperatorActivitySchedulingCapabilityProvider, SequenceOperatorActivitySchedulingCapabilityProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperatorActivitySchedulingPolicy, SequenceOperatorActivitySchedulingPolicy>());
        // spec 123 D2 (ADR 0047 resolution #1): a sequence successor is single-predecessor by construction; this
        // probe lets the ReplaySafe fused completion pump prove it via the spec-119 routing memo.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReplaySafeSuccessorRoutingProbe, SequenceReplaySafeSuccessorRoutingProbe>());
    }
}
