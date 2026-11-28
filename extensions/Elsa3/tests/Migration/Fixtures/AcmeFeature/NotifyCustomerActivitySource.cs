using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;

namespace Acme.Elsa3MigrationFixture;

/// <summary>
/// Catalogs the fixture export's custom Elsa 3 activity under its Elsa 3 type name, <see cref="ActivityTypeKey"/>, so the
/// converter resolves it verbatim as a catalog key. The fixture defines no CLR activity type: a build-folder scan of the
/// test output would find one and catalog it without this feature. The version runs the built-in WriteLine (input
/// <c>text</c>), which is enough for the fixture's definitions to publish; nothing runs them.
/// </summary>
internal sealed class NotifyCustomerActivitySource : IActivityReconciliationSource
{
    /// <summary>The Elsa 3 type name of the fixture's custom activity.</summary>
    public const string ActivityTypeKey = "Acme.NotifyCustomer";

    /// <summary>The canonical alias of the built-in WriteLine activity the version runs.</summary>
    internal const string RuntimeTypeAlias = "Elsa.Activities.Primitives.Activities.WriteLine";

    public string SourceId => "AcmeElsa3MigrationFixture";
    public string SourceKind => "Elsa3MigrationFixture";

    public ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IEnumerable<ActivityVersionReconciliationModel>>([
            new(null, "1.0.0", ActivityTypeKey, "Notify customer", "Acme", "Sends a notification to a customer.",
                "elsa.clr-activity", "1", WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                new ClrActivityDescriptor(RuntimeTypeAlias),
                [new InputDefinition("text", "Text", new TypeReference("String"), null, "Text", null, IsNullable: true, IsRequired: true)],
                [], [])
        ]);
}
