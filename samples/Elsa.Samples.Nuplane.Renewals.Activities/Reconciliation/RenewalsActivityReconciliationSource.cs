using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Samples.Nuplane.Renewals.Activities;

/// <summary>
/// The catalog entry of <see cref="RegisterRenewal"/>. Each release contributes its own activity version, so the catalog keeps the
/// earlier version beside the new one: the reconciler appends a version it has not seen, and refuses one it has seen with
/// other content, so the version must change whenever the inputs do.
/// </summary>
public sealed class RenewalsActivityReconciliationSource : IActivityReconciliationSource
{
    private static readonly ActivityVersionReconciliationModel Activity = new(
        Id: null,
        Version: RenewalsActivitiesRelease.ActivityVersion,
        ActivityTypeKey: typeof(RegisterRenewal).FullName!,
        DisplayName: "Register renewal",
        Category: "Renewals",
        Description: RenewalsActivitiesRelease.Description,
        ProviderKey: "elsa.clr-activity",
        ProviderSchemaVersion: "1",
        ConsumerKey: WellKnownRuntimeActivityConsumers.ClrActivity,
        ConsumerSchemaVersion: RuntimeActivityDescriptor.InitialSchemaVersion,
        Descriptor: new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(RegisterRenewal))),
        Inputs: [StringInput(nameof(RegisterRenewal.PolicyReference), "Policy reference", isRequired: true), .. RenewalsActivitiesRelease.AddedInputs],
        Outputs: [],
        DesignFacets: []);

    public string SourceId => "Elsa.Samples.Nuplane.Renewals.Activities";

    public string SourceKind => "NuplaneSample";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>([Activity]);

    internal static InputDefinition StringInput(string name, string displayName, bool isRequired = false) => new(
        ReferenceKey: name,
        Name: name,
        Type: new TypeReference("String"),
        StorageDriverType: null,
        DisplayName: displayName,
        Category: null,
        IsNullable: !isRequired,
        IsRequired: isRequired);

    internal static InputDefinition DecimalInput(string name, string displayName) => new(
        ReferenceKey: name,
        Name: name,
        Type: new TypeReference("Decimal"),
        StorageDriverType: null,
        DisplayName: displayName,
        Category: null,
        IsNullable: true,
        IsRequired: false);
}
