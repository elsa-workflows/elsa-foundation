using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Samples.Nuplane.Activities.Activities;
using Elsa.Samples.Nuplane.Activities.Descriptors;

namespace Elsa.Samples.Nuplane.Activities.Reconciliation;

public sealed class SampleNuplaneActivityReconciliationSource : IActivityReconciliationSource
{
    private static readonly ActivityVersionReconciliationModel Activity = new(
        Id: null,
        Version: "1.0.0",
        ActivityTypeKey: typeof(SayHelloFromNuplane).FullName!,
        DisplayName: "Say Hello From Nuplane",
        Category: "Samples",
        Description: "Writes a greeting from an activity provided by a Nuplane-loaded package.",
        DescriptorType: typeof(SampleNuplaneActivityDescriptor).FullName!,
        Descriptor: SampleNuplaneActivityDescriptor.Default,
        Inputs:
        [
            new InputDefinition(
                ReferenceKey: nameof(SayHelloFromNuplane.Recipient),
                Name: nameof(SayHelloFromNuplane.Recipient),
                Type: TypeInformation.String,
                StorageDriverType: null,
                DisplayName: "Recipient",
                Category: null,
                IsRequired: false)
        ],
        Outputs: [],
        DesignFacets: []);

    public string SourceId => "Elsa.Samples.Nuplane.Activities";

    public string SourceKind => "NuplaneSample";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>([Activity]);
}
