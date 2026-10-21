using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Samples.Compensation.Activities;

namespace Elsa.Samples.Compensation.Reconciliation;

/// <summary>
/// Adds <see cref="Reserve"/> and <see cref="Release"/> to the activity catalog. A package loaded from a feed contributes
/// its activities explicitly: the host's own CLR source scans only the host's binary folder.
/// </summary>
public sealed class SampleCompensationActivityCatalog : IActivityReconciliationSource
{
    public const string Version = "1.0.0";

    private static readonly ActivityVersionReconciliationModel[] Versions =
    [
        Activity(typeof(Reserve), "Reserve", "Reserves a resource in the sample participant, once per operation key.",
            [
                Input("resource", nameof(Reserve.Resource), "String", "Resource", "The resource to reserve.", isRequired: true),
                Input("reject", nameof(Reserve.Reject), "Boolean", "Reject", "Reject the request, with no effect."),
                Input("loseResponse", nameof(Reserve.LoseResponse), "Boolean", "Lose response", "Apply the reservation, then report an unknown outcome.")
            ],
            [
                new OutputDefinition(ReservationResult.OutputKey, nameof(ReservationResult.Reservation), new TypeReference("String"), null,
                    "Reservation", null, IsNullable: false, Description: "The reservation id, for the compensation to release.", IsRequired: true)
            ]),
        Activity(typeof(Release), "Release", "Releases a reservation as a compensation, or refuses it if it arrives later.",
            [
                Input("resource", nameof(Release.Resource), "String", "Resource", "The resource whose reservation to release.", isRequired: true),
                Input("reservation", nameof(Release.Reservation), "String", "Reservation", "The reservation the step reported (its result)."),
                Input("failTimes", nameof(Release.FailTimes), "Int32", "Fail times", "How many calls fail before one works.")
            ],
            [])
    ];

    public string SourceId => "Elsa.Samples.Compensation";

    public string SourceKind => "NuplaneSample";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>(Versions);

    private static ActivityVersionReconciliationModel Activity(Type type, string displayName, string description,
        InputDefinition[] inputs, OutputDefinition[] outputs) =>
        new(
            Id: null,
            Version: Version,
            ActivityTypeKey: type.FullName!,
            DisplayName: displayName,
            Category: "Samples",
            Description: description,
            ProviderKey: "elsa.clr-activity",
            ProviderSchemaVersion: "1",
            ConsumerKey: WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion: RuntimeActivityDescriptor.InitialSchemaVersion,
            Descriptor: new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(type)),
            Inputs: inputs,
            Outputs: outputs,
            DesignFacets: []);

    private static InputDefinition Input(string key, string name, string alias, string displayName, string description, bool isRequired = false) =>
        new(key, name, new TypeReference(alias), null, displayName, null, IsNullable: !isRequired, Description: description, IsRequired: isRequired);
}
