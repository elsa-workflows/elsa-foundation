using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Agent.Handlers;

/// <summary>The single Sequential context aggregator; modules register sources rather than event handlers.</summary>
internal sealed class CollectAgentContext(IEnumerable<IAgentContextProvider> sources) : IEventHandler<AgentContextCollecting>
{
    public async Task Handle(AgentContextCollecting domainEvent, CancellationToken cancellationToken)
    {
        var registered = sources.ToList();
        var duplicate = registered.GroupBy(x => x.Descriptor.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new AgentContributionException($"Duplicate agent context source '{duplicate.Key}'.");

        var request = domainEvent.Request;
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in registered)
        {
            var descriptor = source.Descriptor;
            var caller = request.CallerAttachments.Where(x => x.ModuleId == descriptor.ModuleId && descriptor.ContextKinds.Contains(x.ContentType, StringComparer.Ordinal)).ToList();
            var applicable = descriptor.ScopeKind == "*" || caller.Count > 0 || descriptor.ResourceTypes.Contains(request.Target?.ResourceType ?? string.Empty, StringComparer.Ordinal)
                || (!string.IsNullOrEmpty(request.ScopeKind) && string.Equals(descriptor.ScopeKind, request.ScopeKind, StringComparison.Ordinal));
            if (!applicable)
                continue;
            var admittedInputs = new List<AgentContextAttachment>();
            foreach (var attachment in caller)
            {
                claimed.Add(attachment.Id);
                if (!descriptor.SchemaVersions.Contains(attachment.SchemaVersion, StringComparer.Ordinal))
                    domainEvent.Diagnostics.Add(new(descriptor.Id, "agent.context.version_unsupported", "The installed module does not support this context schema version.", true));
                else
                    admittedInputs.Add(attachment with
                    {
                        Origin = AgentContextOrigin.ClientSnapshot,
                        AgentScopes = [],
                        // A caller's redaction claim is not evidence that its body is redacted.
                        Sensitivity = attachment.Sensitivity == AgentContextSensitivity.SecretRedacted ? AgentContextSensitivity.Secret : attachment.Sensitivity
                    });
            }
            var contribution = await source.CollectAsync(request with { CallerAttachments = admittedInputs }, cancellationToken);
            foreach (var diagnostic in contribution.Diagnostics)
                domainEvent.Diagnostics.Add(diagnostic);
            foreach (var attachment in contribution.Attachments)
            {
                if (attachment.ModuleId != descriptor.ModuleId)
                    throw new AgentContributionException($"Context source '{descriptor.Id}' returned another module's context.");
                domainEvent.Attachments.Add(attachment);
            }
        }
        foreach (var attachment in request.CallerAttachments.Where(x => !claimed.Contains(x.Id)))
            domainEvent.Diagnostics.Add(new(attachment.ModuleId, "agent.context.kind_unsupported", "No installed module admits the supplied context kind and owner.", true));
        var duplicateAttachment = domainEvent.Attachments.GroupBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicateAttachment is not null)
            throw new AgentContributionException($"Duplicate admitted context attachment '{duplicateAttachment.Key}'.");
    }
}

/// <summary>The single Sequential capability aggregator over installed providers.</summary>
internal sealed class CollectAgentCapabilities(IEnumerable<IAgentCapabilityProvider> sources) : IEventHandler<AgentCapabilitiesCollecting>
{
    public async Task Handle(AgentCapabilitiesCollecting domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            foreach (var capability in await source.GetCapabilitiesAsync(cancellationToken))
                domainEvent.Capabilities.Add(capability);
        var duplicate = domainEvent.Capabilities.GroupBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new AgentContributionException($"Duplicate agent capability '{duplicate.Key}'.");
    }
}

/// <summary>The single Sequential tool aggregator over the existing installed tool contract.</summary>
internal sealed class CollectAgentTools(IEnumerable<IAgentTool> tools) : IEventHandler<AgentToolsCollecting>
{
    public Task Handle(AgentToolsCollecting domainEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var tool in tools)
            domainEvent.Tools.Add(tool);
        return Task.CompletedTask;
    }
}
