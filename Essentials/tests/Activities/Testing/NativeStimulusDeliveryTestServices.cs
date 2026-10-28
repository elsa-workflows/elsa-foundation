using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Testing;

/// <summary>
/// Explicit native component-fixture composition for named-event publication. The schema facts are constructed
/// for fresh in-memory stores; they are not evidence of EF finalization, fleet admission or host recovery.
/// Existing caller-owned dormancy/access services remain authoritative. Ordinary harness defaults stay unqualified.
/// </summary>
public static class NativeStimulusDeliveryTestServices
{
    public static void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISchemaDormancyCheck>(_ => new SchemaDormancyCheck(observed: new FinalizedNativeSchema()));
        services.TryAddSingleton<IPersistenceAccessContextAccessor, NativeAccess>();
        services.AddWorkflowStimulusRouter();
    }

    private sealed class NativeAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope(PersistenceScope.DefaultValue));
    }

    private sealed class FinalizedNativeSchema : IObservedSchemaFinalization
    {
        private static readonly SchemaFamilyObservation Observation = new("RuntimePostCommitOutbox", "native-component-test",
            ["1.0.0", "2.0.0"], "2.0.0", "2.0.0", false, null, [], null, WorkflowExecutionHarness.Timestamp);
        public SchemaFamilyObservation? Find(string family) => family == Observation.Family ? Observation : null;
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [Observation];
        public ValueTask RefreshAsync(string family, TimeSpan maxAge, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe());
    }
}
