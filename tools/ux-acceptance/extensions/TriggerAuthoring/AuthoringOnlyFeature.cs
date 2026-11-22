using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.TriggerAuthoring;

/// <summary>An explicit, process-local authoring replacement. It installs no Runtime, queue or source listener.</summary>
[ShellFeature("AcceptanceTriggerAuthoringOnly", DisplayName = "Owned authoring-only trigger store")]
public sealed class AuthoringOnlyFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.TryAddSingleton<IStandaloneTriggerStore, AuthoringStore>();
        services.TryAddSingleton<IStandaloneTriggerTargetDiscovery, NoServingTargets>();
        services.TryAddSingleton<ITriggerInputMapper, NoServingInputs>();
    }
}

internal sealed class NoServingTargets : IStandaloneTriggerTargetDiscovery
{
    public ValueTask<TriggerTargetPage> ListAsync(TriggerScope scope, TriggerTargetQuery query, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new TriggerTargetPage([], null)); }
    public ValueTask<TriggerTargetDescription?> DescribeAsync(TriggerScope scope, string definitionId, string slotName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<TriggerTargetDescription?>(new(definitionId, slotName, definitionId, "native", TriggerTargetState.Unpublished,
            null, null, null, null, null, null, false, [new("AuthoringOnly", "This authoring host has no serving target or input contract.")]));
    }
}
internal sealed class NoServingInputs : ITriggerInputMapper
{
    public WorkflowExecutableInputValidation ValidateMappings(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings) =>
        WorkflowExecutableInputValidation.Failure([new("authoring-only", "This host cannot validate serving inputs or admit work.")]);
    public ValueTask<WorkflowExecutableInputValidation> MapAsync(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings,
        JsonElement sourceEnvelope, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(ValidateMappings(contract, mappings)); }
}

/// <summary>Owned demonstration store: scoped/idempotent paused metadata only, intentionally not durable across processes.</summary>
internal sealed class AuthoringStore(TimeProvider clock) : IStandaloneTriggerStore
{
    private readonly object sync = new();
    private readonly Dictionary<(TriggerScope Scope, string Id), TriggerAggregate> resources = [];
    private readonly Dictionary<(TriggerScope Scope, string Actor, string Request), TriggerMutationReceipt> mutations = [];
    public ValueTask<TriggerAggregate?> FindAsync(TriggerScope scope, string triggerId, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); lock (sync) return ValueTask.FromResult(resources.GetValueOrDefault((scope, triggerId))); }
    public ValueTask<TriggerSubscription?> FindSubscriptionAsync(TriggerScope scope, string subscriptionId, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); lock (sync) return ValueTask.FromResult(Rows(scope).Where(row => row.Trigger.DesiredState != TriggerDesiredState.Deleted).SelectMany(row => row.Subscriptions).SingleOrDefault(row => row.Id == subscriptionId)); }
    public ValueTask<TriggerResourcePage> ListAsync(TriggerScope scope, TriggerResourceQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var rows = Rows(scope).Select(row => row.Trigger).Where(row => row.DesiredState != TriggerDesiredState.Deleted &&
                (query.After is null || StringComparer.Ordinal.Compare(row.Id, query.After) > 0) &&
                (string.IsNullOrWhiteSpace(query.Search) || row.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase)) &&
                (query.TypeKey is null || row.TypeKey == query.TypeKey) && (query.DesiredState is null || row.DesiredState == query.DesiredState))
                .OrderBy(row => row.Id, StringComparer.Ordinal).Take(query.Limit + 1).ToArray();
            return ValueTask.FromResult(new TriggerResourcePage(rows.Take(query.Limit).ToArray(), rows.Length > query.Limit ? rows[query.Limit - 1].Id : null));
        }
    }
    public ValueTask<TriggerSubscriptionPage> ListSubscriptionsAsync(TriggerScope scope, TriggerSubscriptionQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            var rows = Rows(scope).Where(row => row.Trigger.DesiredState != TriggerDesiredState.Deleted).SelectMany(row => row.Subscriptions)
                .Where(row => (query.TriggerId is null || row.TriggerId == query.TriggerId) && (query.DefinitionId is null || row.DefinitionId == query.DefinitionId) &&
                    (query.SlotName is null || row.SlotName == query.SlotName) && (query.After is null || StringComparer.Ordinal.Compare(row.Id, query.After) > 0))
                .OrderBy(row => row.Id, StringComparer.Ordinal).Take(query.Limit + 1).ToArray();
            return ValueTask.FromResult(new TriggerSubscriptionPage(rows.Take(query.Limit).ToArray(), rows.Length > query.Limit ? rows[query.Limit - 1].Id : null));
        }
    }
    public ValueTask<TriggerMutationReceipt?> FindMutationAsync(TriggerScope scope, string actorId, string requestId, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); lock (sync) return ValueTask.FromResult(mutations.GetValueOrDefault((scope, actorId, requestId))); }
    public ValueTask<TriggerMutationReceipt> WriteAsync(TriggerScope scope, TriggerStoreWrite write, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (mutations.TryGetValue((scope, write.ActorId, write.RequestId), out var receipt))
                return receipt.RequestFingerprint == write.RequestFingerprint ? ValueTask.FromResult(receipt) : throw new TriggerOperationException("conflict", "The mutation key has different content.");
            if (write.Trigger.HasActivated || write.Trigger.DesiredState == TriggerDesiredState.Enabled || write.Subscriptions.Any(row => row.Enabled))
                throw new TriggerOperationException("unavailable", "This process-local authoring store accepts only never-activated paused metadata.");
            var previous = resources.GetValueOrDefault((scope, write.Trigger.Id));
            if (previous?.Trigger.Revision != write.ExpectedRevision) throw new TriggerOperationException("conflict", "The resource revision changed.");
            resources[(scope, write.Trigger.Id)] = new(write.Trigger, Array.AsReadOnly(write.Subscriptions.ToArray()));
            receipt = new(write.RequestId, write.RequestFingerprint, write.ActorId, write.Trigger.Id, write.Trigger.Revision, clock.GetUtcNow());
            mutations.Add((scope, write.ActorId, write.RequestId), receipt);
            return ValueTask.FromResult(receipt);
        }
    }
    private IEnumerable<TriggerAggregate> Rows(TriggerScope scope) => resources.Where(pair => pair.Key.Scope == scope).Select(pair => pair.Value);
}
