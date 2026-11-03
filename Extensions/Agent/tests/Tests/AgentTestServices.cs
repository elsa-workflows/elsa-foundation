using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Elsa.Agent.Tests;

/// <summary>Real named-event/default composition; tests supply authority explicitly instead of weakening defaults.</summary>
internal static class AgentTestServices
{
    public static ServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddFoundationAgentAbstractions();
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer, AllowTestAgentToolAuthorizer>());
        AddToolResultSource(services);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static void AddToolResultSource(IServiceCollection services)
        => services.AddScoped<IAgentContextProvider>(provider => new AgentTestContextSource(
            AgentTestContextSource.TestModuleSourceId, "test.module", [], provider.GetServices<IAgentTool>()));
}

/// <summary>Installed test-module admission over fixture-owned evidence and actual registered tool contracts.</summary>
internal sealed class AgentTestContextSource(string id, string moduleId, IReadOnlyCollection<AgentContextAttachment> context,
    IEnumerable<IAgentTool>? tools = null) : IAgentContextProvider
{
    public const string TestModuleSourceId = "test.module.disclosure";
    public AgentContextProviderDescriptor Descriptor { get; } = new(id, moduleId, id,
        context.Select(item => item.ContentType).Distinct(StringComparer.Ordinal).ToArray(), ["1"],
        context.Where(item => item.Target is not null).Select(item => item.Target!.ResourceType).Distinct(StringComparer.Ordinal).ToArray());
    public string ScopeKind => Descriptor.ScopeKind;

    public ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AgentContextContribution(context, []));
    }

    public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invalidContext = request.Context.Any(item => item.ModuleId != moduleId || !context.Any(owned =>
            JsonSerializer.Serialize(owned with { ContributorId = string.Empty }) == JsonSerializer.Serialize(item with { ContributorId = string.Empty })));
        return ValueTask.FromResult(invalidContext || request.Results.Any(result => !OwnsResult(result))
            ? new AgentContextContribution([], [new(id, "test.disclosure_not_owned", "This fixture source does not own the requested evidence.", true)])
            : new(request.Context, []));
    }

    private bool OwnsResult(AgentToolResult result)
    {
        var descriptor = tools?.SingleOrDefault(tool => tool.Descriptor.Name == result.ToolName && tool.Descriptor.ModuleId == moduleId)?.Descriptor;
        if (descriptor is null || descriptor.DisclosureSourceId != id
            || result.Access is not { } access || access.ModuleId != moduleId || access.DisclosureSourceId != id
            || access.ContractVersion != descriptor.ContractVersion || access.Target is { } target && target.ModuleId != moduleId)
            return false;
        if (result.Payload is { } payload && (descriptor.ResultType != payload.TypeId || descriptor.ResultSchemaVersion != payload.SchemaVersion))
        {
            // The engine's refusal/uncertainty envelope is distinct from this fixture's applied
            // resource schema. Admit only the exact original invocation's bounded lifecycle shape.
            if (result.Succeeded || result.Receipt is not null || payload.TypeId != "agent.operation-outcome" || payload.SchemaVersion != "1"
                || payload.Data.ValueKind != JsonValueKind.Object || payload.Data.EnumerateObject().Count() != 2
                || !payload.Data.TryGetProperty("status", out var status) || status.GetString() is not ("denied" or "refused" or "uncertain")
                || !payload.Data.TryGetProperty(nameof(AgentPreparedToolInvocation.InvocationId), out var identity) || identity.GetString() != access.InvocationId)
                return false;
        }
        return result.Receipt is not { } receipt || receipt.Before == access.Target && receipt.After.ModuleId == moduleId
            && receipt.Before.ResourceType == receipt.After.ResourceType && receipt.Before.ResourceId == receipt.After.ResourceId;
    }
}

internal sealed class AllowTestAgentToolAuthorizer : IAgentToolAuthorizer
{
    public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId,
        AgentResourceTarget? target, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentPolicyDecision(true, []));
}
