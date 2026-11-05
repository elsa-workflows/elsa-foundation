using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core;
using Elsa.Agent.Extensions;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Services;

/// <summary>Checks the selected effects feature through the shared schema observation at each acceptance boundary.</summary>
internal sealed class AgentEffectsAdmission(AgentRegistrationValidator registrations, IServiceProvider services, AgentPrivilegePolicy privileges)
{
    internal ValueTask<AgentError?> CheckPreparationAsync(AgentToolDescriptor descriptor, CancellationToken cancellationToken = default)
        => CheckPrivilegesAsync(null, descriptor.Risk, UsesNewContract(descriptor) || descriptor.Risk == AgentRisk.Admin,
            $"Tool '{descriptor.Name}'", cancellationToken);

    internal ValueTask<AgentError?> CheckInvocationAsync(AgentPreparedToolInvocation prepared,
        AgentToolDescriptor? descriptor = null, CancellationToken cancellationToken = default)
        => CheckPrivilegesAsync(prepared.RequiredPrivilegeClasses, prepared.Risk,
            prepared.RequiredPrivilegeClasses is { Count: > 0 } || descriptor is not null && UsesNewContract(descriptor),
            $"Prepared invocation '{prepared.InvocationId}'", cancellationToken);

    /// <summary>Approval reads retained core meaning only; it never resolves a tool or a principal.</summary>
    internal ValueTask<AgentError?> CheckReviewAsync(AgentActionProposal proposal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Shared feature/schema observation belongs to acceptance and dispatch, not the human decision.
        // Approval must still work when an owner or its schema observation became unavailable after review.
        return ValueTask.FromResult(privileges.Check(proposal.PreparedInvocation?.RequiredPrivilegeClasses,
            proposal.PreparedInvocation?.Risk ?? proposal.Risk));
    }

    internal static bool UsesNewContract(AgentToolDescriptor descriptor) => descriptor.CanPerformWithoutPersistence || descriptor.ModuleCodes is not null;

    private async ValueTask<AgentError?> CheckPrivilegesAsync(IReadOnlyList<AgentPrivilegeClass>? requirements, AgentRisk risk,
        bool needsEffects, string subject, CancellationToken cancellationToken)
    {
        Task<AgentError?>? pending = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (privileges.Check(requirements, risk) is { } veto) return veto;
            AgentError? unavailable = null;
            if (needsEffects)
            {
                pending = CheckAsync(cancellationToken).AsTask();
                unavailable = await pending.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (unavailable is not null) return unavailable;
            // A shared refresh can await I/O. The host setting observed before it is no longer current authority.
            return privileges.Check(requirements, risk);
        }
        catch (Exception error) when (error is not (OperationCanceledException or AgentContributionException))
        {
            // Context collection calls CheckAsync directly and keeps its optional/required failure policy.
            throw new AgentContributionException($"{subject} effects admission could not be established.", error);
        }
        finally
        {
            if (pending is not null) _ = ObserveLateAdmissionAsync(pending);
        }
    }

    private static async Task ObserveLateAdmissionAsync(Task pending)
    {
        // Stop ends admission independently of the observer; a late answer cannot dispatch an owner callback.
        try { await pending; }
        catch (Exception) { }
    }

    internal async ValueTask<AgentError?> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = registrations.GetEffectsSelection();
        if (selection is null)
            return new("agent.effects.not_enabled", "The selected host has not enabled the generic effects contract.", 409);
        if (selection.Requirements.Count == 0) return null;

        // Resolving the shared checker only when required keeps memory-only hosts observation-free.
        var checks = services.GetServices<ISchemaDormancyCheck>().Take(2).ToArray();
        var availability = checks.Length == 1
            ? await checks[0].EvaluateAsync(selection.Requirements, cancellationToken)
            : SchemaDormancyRule.Evaluate(selection.Requirements, _ => null);
        return availability.IsAvailable ? null : new("schema-version-not-finalized", availability.Reason, 409);
    }

    internal static void Register(IServiceCollection services, Type featureType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(featureType);
        services.AddSingleton(new AgentEffectsSelection(featureType,
            Array.AsReadOnly(SchemaVersionRequirement.DeclaredBy(featureType).ToArray())));
    }

    internal static AgentEffectsSelection? ValidateSelection(IServiceCollection services)
    {
        var descriptors = services.Where(item => item.ServiceType == typeof(AgentEffectsSelection)).ToArray();
        if (descriptors.Length == 0) return null;
        if (descriptors.Length != 1)
            throw new InvalidOperationException("Agent effects require one selected feature variation. Remove duplicate selections; do not select both the common feature and a store-specific variation.");
        if (descriptors[0].IsKeyedService || descriptors[0].Lifetime != ServiceLifetime.Singleton
            || descriptors[0].ImplementationInstance is not AgentEffectsSelection selection
            || !typeof(FoundationAgentEffectsFeature).IsAssignableFrom(selection.FeatureType))
            throw new InvalidOperationException("Agent effects selection must be owned by the selected effects feature's ConfigureServices registration.");

        var requiredType = AgentPersistenceSelection.RequiredEffectsFeatureType(services);
        if (requiredType is null)
            throw new InvalidOperationException("The selected Agent record store has not declared effects support. Use SelectAgentSessionRecordStore(factory, requiredEffectsFeatureType), or omit the effects feature for a legacy-only composition.");
        if (!requiredType.IsAssignableFrom(selection.FeatureType))
            throw new InvalidOperationException($"The selected Agent record store requires an effects feature derived from '{requiredType.FullName}', but '{selection.FeatureType.FullName}' was selected.");

        // Attributes are deliberately not inherited. A specialization must repeat every base minimum directly,
        // so the shared catalog, Attention and execution all see the same requirements on the actual type.
        var declared = SchemaVersionRequirement.DeclaredBy(selection.FeatureType);
        for (var type = selection.FeatureType.BaseType; type is not null && typeof(FoundationAgentEffectsFeature).IsAssignableFrom(type); type = type.BaseType)
            if (SchemaVersionRequirement.DeclaredBy(type).Any(minimum => !declared.Contains(minimum)))
                throw new InvalidOperationException($"Agent effects feature '{selection.FeatureType.FullName}' must directly repeat the schema requirements declared by '{type.FullName}'.");
        if (SchemaVersionRequirement.DeclaredBy(requiredType).Any(minimum => !declared.Contains(minimum)))
            throw new InvalidOperationException($"Agent effects feature '{selection.FeatureType.FullName}' must directly declare the selected store's minimum schema requirements.");
        if (!selection.Requirements.SequenceEqual(declared))
            throw new InvalidOperationException("Agent effects selection metadata does not match the actual feature's directly declared schema requirements.");
        return selection;
    }
}

/// <summary>Immutable feature configuration; it carries no current authority or observed schema state.</summary>
internal sealed record AgentEffectsSelection(Type FeatureType, IReadOnlyList<SchemaVersionRequirement> Requirements);
