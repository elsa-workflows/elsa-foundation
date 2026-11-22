using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Extensions;
using Elsa.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Elsa.Weaver.Tests;

/// <summary>Real named-event/default composition; tests supply authority explicitly instead of weakening defaults.</summary>
internal static class WeaverTestServices
{
    public static ServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddWeaver();
        services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer, AllowTestWeaverToolAuthorizer>());
        AddToolResultSource(services);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static void AddToolResultSource(IServiceCollection services)
        => services.AddScoped<IWeaverContextProvider>(provider => new WeaverTestContextSource(
            WeaverTestContextSource.TestModuleSourceId, "test.module", [], provider.GetServices<IWeaverTool>()));
}

/// <summary>Installed test-module admission over fixture-owned evidence and actual registered tool contracts.</summary>
internal sealed class WeaverTestContextSource(string id, string moduleId, IReadOnlyCollection<WeaverContextAttachment> context,
    IEnumerable<IWeaverTool>? tools = null) : IWeaverContextProvider
{
    public const string TestModuleSourceId = "test.module.disclosure";
    public WeaverContextProviderDescriptor Descriptor { get; } = new(id, moduleId, id,
        context.Select(item => item.ContentType).Distinct(StringComparer.Ordinal).ToArray(), ["1"],
        context.Where(item => item.Target is not null).Select(item => item.Target!.ResourceType).Distinct(StringComparer.Ordinal).ToArray());
    public string ScopeKind => Descriptor.ScopeKind;

    public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new WeaverContextContribution(context, []));
    }

    public ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var invalidContext = request.Context.Any(item => item.ModuleId != moduleId || !context.Any(owned =>
            JsonSerializer.Serialize(owned with { ContributorId = string.Empty }) == JsonSerializer.Serialize(item with { ContributorId = string.Empty })));
        return ValueTask.FromResult(invalidContext || request.Results.Any(result => !OwnsResult(result))
            ? new WeaverContextContribution([], [new(id, "test.disclosure_not_owned", "This fixture source does not own the requested evidence.", true)])
            : new(request.Context, []));
    }

    private bool OwnsResult(WeaverToolResult result)
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
            if (result.Succeeded || result.Receipt is not null || payload.TypeId != "weaver.operation-outcome" || payload.SchemaVersion != "1"
                || payload.Data.ValueKind != JsonValueKind.Object || payload.Data.EnumerateObject().Count() != 2
                || !payload.Data.TryGetProperty("status", out var status) || status.GetString() is not ("denied" or "refused" or "uncertain")
                || !payload.Data.TryGetProperty(nameof(WeaverPreparedToolInvocation.InvocationId), out var identity) || identity.GetString() != access.InvocationId)
                return false;
        }
        return result.Receipt is not { } receipt || receipt.Before == access.Target && receipt.After.ModuleId == moduleId
            && receipt.Before.ResourceType == receipt.After.ResourceType && receipt.Before.ResourceId == receipt.After.ResourceId;
    }
}

internal sealed class AllowTestWeaverToolAuthorizer : IWeaverToolAuthorizer
{
    public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId,
        WeaverResourceTarget? target, CancellationToken cancellationToken = default)
        => Task.FromResult(new WeaverPolicyDecision(true, []));
}
