using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Samples.Nuplane.Notes.Activities;

/// <summary>
/// The catalog entry of <see cref="AddNote"/>. Each release contributes its own activity version, so the catalog keeps the
/// earlier version beside the new one: the reconciler appends a version it has not seen, and refuses one it has seen with
/// other content, so the version must change whenever the inputs do.
/// </summary>
public sealed class NotesActivityReconciliationSource : IActivityReconciliationSource
{
    private static readonly ActivityVersionReconciliationModel Activity = new(
        Id: null,
        Version: NotesActivitiesRelease.ActivityVersion,
        ActivityTypeKey: typeof(AddNote).FullName!,
        DisplayName: "Add note",
        Category: "Notes",
        Description: NotesActivitiesRelease.Description,
        ProviderKey: "elsa.clr-activity",
        ProviderSchemaVersion: "1",
        ConsumerKey: WellKnownRuntimeActivityConsumers.ClrActivity,
        ConsumerSchemaVersion: RuntimeActivityDescriptor.InitialSchemaVersion,
        Descriptor: new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(AddNote))),
        Inputs: [StringInput(nameof(AddNote.Text), "Text", isRequired: true), .. NotesActivitiesRelease.AddedInputs],
        Outputs: [],
        DesignFacets: []);

    public string SourceId => "Elsa.Samples.Nuplane.Notes.Activities";

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
}
