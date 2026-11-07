using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Elsa.Agent.Handlers;

/// <summary>The single Sequential context aggregator; modules register sources rather than event handlers.</summary>
internal sealed class CollectAgentContext(IEnumerable<IAgentContextProvider> sources, IOptions<AgentContextOptions> options,
    TimeProvider timeProvider, Services.AgentEffectsAdmission effects, IAgentToolRegistry tools) : IEventHandler<AgentContextCollecting>
{
    private sealed record RegisteredSource(IAgentContextProvider Provider, AgentContextProviderDescriptor Descriptor);

    public async Task Handle(AgentContextCollecting domainEvent, CancellationToken cancellationToken)
    {
        var request = domainEvent.Request;
        var callerIds = new HashSet<string>(StringComparer.Ordinal);
        if (request.CallerAttachments.Any(attachment => !callerIds.Add(attachment.Id)))
        {
            domainEvent.Diagnostics.Add(new("host", "agent.context.duplicate_id", "Caller context attachment IDs must be unique within one prompt.", true));
            return;
        }

        var registered = sources.Select(source => new RegisteredSource(source, source.Descriptor)).ToList();
        foreach (var registration in registered) Services.AgentModuleCodes.Validate(registration.Descriptor.ModuleCodes);
        var duplicate = registered.GroupBy(x => x.Descriptor.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new AgentContributionException($"Duplicate agent context source '{duplicate.Key}'.");

        if (request.Disclosure is { } disclosure)
        {
            await AdmitDisclosureAsync(registered, request, disclosure, domainEvent, cancellationToken);
            return;
        }

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in registered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = registration.Provider;
            var descriptor = registration.Descriptor;
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
            var contribution = await CollectSourceAsync(source, descriptor, request with { CallerAttachments = admittedInputs }, domainEvent.Diagnostics, cancellationToken);
            if (contribution is null)
                continue;
            foreach (var diagnostic in contribution.Diagnostics)
                Add(domainEvent, diagnostic, descriptor);
            foreach (var attachment in contribution.Attachments)
            {
                if (attachment.ModuleId != descriptor.ModuleId)
                    throw new AgentContributionException($"Context source '{descriptor.Id}' returned another module's context.");
                domainEvent.Attachments.Add(Snapshot(attachment) with { ContributorId = descriptor.Id });
            }
        }
        foreach (var attachment in request.CallerAttachments.Where(x => !claimed.Contains(x.Id)))
            domainEvent.Diagnostics.Add(new(attachment.ModuleId, "agent.context.kind_unsupported", "No installed module admits the supplied context kind and owner.", true));
        var duplicateAttachment = domainEvent.Attachments.GroupBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicateAttachment is not null)
            throw new AgentContributionException($"Duplicate admitted context attachment '{duplicateAttachment.Key}'.");
    }

    private async Task AdmitDisclosureAsync(IReadOnlyList<RegisteredSource> registered, AgentContextRequest request,
        AgentContextDisclosureRequest disclosure, AgentContextCollecting domainEvent, CancellationToken cancellationToken)
    {
        var sourceIds = disclosure.Context.Select(x => x.ContributorId)
            .Concat(disclosure.Results.Select(x => x.Access?.DisclosureSourceId)).Distinct(StringComparer.Ordinal);
        foreach (var sourceId in sourceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var registration = registered.SingleOrDefault(x => x.Descriptor.Id == sourceId);
            var source = registration?.Provider;
            var descriptor = registration?.Descriptor;
            var ownedContext = disclosure.Context.Where(x => x.ContributorId == sourceId).ToArray();
            var ownedResults = disclosure.Results.Where(x => x.Access?.DisclosureSourceId == sourceId).ToArray();
            if (source is null || descriptor is null || ownedContext.Any(x => x.ModuleId != descriptor.ModuleId
                    || !descriptor.ContextKinds.Contains(x.ContentType, StringComparer.Ordinal)
                    || !descriptor.SchemaVersions.Contains(x.SchemaVersion, StringComparer.Ordinal))
                || ownedResults.Any(x => x.Access?.ModuleId != descriptor.ModuleId))
            {
                domainEvent.Diagnostics.Add(new("host", "agent.context.disclosure_unavailable",
                    "The original installed context source, owner or schema is unavailable. Start a fresh request with current modules.", true));
                continue;
            }
            var owned = disclosure with
            {
                Context = ownedContext.Select(Snapshot).ToArray(),
                Results = ownedResults
            };
            var contribution = await CollectSourceAsync(source, descriptor, request with { CallerAttachments = [], Disclosure = owned },
                domainEvent.Diagnostics, cancellationToken);
            if (contribution is null) continue;
            foreach (var diagnostic in contribution.Diagnostics) Add(domainEvent, diagnostic, descriptor);
            foreach (var attachment in contribution.Attachments)
            {
                if (attachment.ModuleId != descriptor.ModuleId
                    || !descriptor.ContextKinds.Contains(attachment.ContentType, StringComparer.Ordinal)
                    || !descriptor.SchemaVersions.Contains(attachment.SchemaVersion, StringComparer.Ordinal))
                    throw new AgentContributionException($"Context source '{descriptor.Id}' returned an invalid disclosure projection.");
                domainEvent.Attachments.Add(Snapshot(attachment) with { ContributorId = descriptor.Id });
            }
        }
        if (domainEvent.Attachments.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new AgentContributionException("Retained context disclosure produced duplicate attachment identities.");
    }

    private async Task<AgentContextContribution?> CollectSourceAsync(IAgentContextProvider source, AgentContextProviderDescriptor descriptor, AgentContextRequest request,
        ICollection<AgentContributionDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(options.Value.SourceTimeout, timeProvider);
        using var sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Task? pending = null;
        try
        {
            var needsEffects = descriptor.ModuleCodes is not null;
            if (request.Disclosure is { Results.Count: > 0 } retainedDisclosure)
            {
                // New retained results need current feature admission even when their disclosure source is legacy.
                // Installed metadata is not execution authority; only the shared feature check is performed here.
                var metadata = tools.ListAsync(sourceCancellation.Token);
                pending = metadata;
                var installed = (await metadata.WaitAsync(sourceCancellation.Token)).Select(tool => tool.Descriptor).ToArray();
                needsEffects |= retainedDisclosure.Results.Any(result => result.Receipt is { ModuleCode: not null }
                    or { Scope: AgentEffectScope.NoPersistedEffect }
                    || installed.Any(tool => tool.Name == result.ToolName
                        && tool.ModuleId == result.Access?.ModuleId && Services.AgentEffectsAdmission.UsesNewContract(tool)));
            }
            if (needsEffects && await CheckAdmissionAsync() is { } unavailable)
            {
                diagnostics.Add(new(descriptor.Id, unavailable.Code, unavailable.Message, request.Disclosure is not null || descriptor.Required));
                return null;
            }
            sourceCancellation.Token.ThrowIfCancellationRequested();
            var sourceTask = request.Disclosure is { } disclosure
                ? source.AdmitDisclosureAsync(disclosure, sourceCancellation.Token).AsTask()
                : source.CollectAsync(request, sourceCancellation.Token).AsTask();
            pending = sourceTask;
            var contribution = await sourceTask.WaitAsync(sourceCancellation.Token);
            sourceCancellation.Token.ThrowIfCancellationRequested();
            // A source may await I/O while the shared observation changes. Its returned payload is not yet admitted.
            if (needsEffects && await CheckAdmissionAsync() is { } changed)
            {
                diagnostics.Add(new(descriptor.Id, changed.Code, changed.Message, request.Disclosure is not null || descriptor.Required));
                return null;
            }
            sourceCancellation.Token.ThrowIfCancellationRequested();
            return contribution;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            diagnostics.Add(new(descriptor.Id, "agent.context.source_timed_out",
                "The installed context source did not complete within the host's collection bound.", request.Disclosure is not null || descriptor.Required));
            return null;
        }
        catch (Exception error) when (request.Disclosure is null && !descriptor.Required && error is not AgentContributionException)
        {
            diagnostics.Add(new(descriptor.Id, "agent.context.source_failed",
                "Optional installed context is unavailable because its source failed.", false));
            return null;
        }
        catch (Exception error)
        {
            throw new AgentContributionException($"Required context source '{descriptor.Id}' failed.", error);
        }
        finally
        {
            if (pending is not null)
                _ = ObserveLateCompletionAsync(pending);
        }

        async Task<AgentError?> CheckAdmissionAsync()
        {
            var check = effects.CheckAsync(sourceCancellation.Token).AsTask();
            pending = check;
            var result = await check.WaitAsync(sourceCancellation.Token);
            sourceCancellation.Token.ThrowIfCancellationRequested();
            return result;
        }
    }

    private static async Task ObserveLateCompletionAsync(Task pending)
    {
        // An owner, registry or shared observer may ignore cancellation. Late data never rejoins admission.
        try { await pending; }
        catch (Exception) { }
    }

    // Only this handler records markers, and only for a source's own diagnostics: another event handler that adds a
    // marked diagnostic to the public collection is never recorded, so the collector clears its marker.
    private static void Add(AgentContextCollecting domainEvent, AgentContributionDiagnostic diagnostic, AgentContextProviderDescriptor descriptor)
    {
        var owned = diagnostic.ContributorId == descriptor.Id && diagnostic.ContributorId != "host";
        var normalized = Services.AgentModuleCodes.Diagnostic(diagnostic, descriptor.ModuleCodes, owned);
        domainEvent.Diagnostics.Add(normalized);
        if (owned) Services.AgentContextOwnerMarkers.Record(domainEvent, normalized, descriptor.ModuleCodes is not null);
    }

    private static AgentContextAttachment Snapshot(AgentContextAttachment attachment)
    {
        try
        {
            return attachment with
            {
                Content = attachment.Content is null || attachment.Sensitivity == AgentContextSensitivity.Secret
                    ? null : JsonSerializer.SerializeToElement(attachment.Content).Clone(),
                References = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(attachment.References.ToDictionary()),
                AgentScopes = Array.AsReadOnly(attachment.AgentScopes.ToArray())
            };
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new AgentContributionException("A context source returned content that cannot be transported as JSON.", error);
        }
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
internal sealed class CollectAgentTools(IEnumerable<IAgentTool> tools, IEnumerable<IAgentContextProvider> sources) : IEventHandler<AgentToolsCollecting>
{
    public Task Handle(AgentToolsCollecting domainEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = tools.ToArray();
        // Registration integrity uses only public metadata. Context/disclosure behavior remains
        // exclusively on AgentContextCollecting and is rechecked under current authority there.
        var descriptors = sources.Select(source => source.Descriptor).ToArray();
        foreach (var source in descriptors) Services.AgentModuleCodes.Validate(source.ModuleCodes);
        foreach (var tool in installed)
        {
            var descriptor = tool.Descriptor;
            Services.AgentModuleCodes.Validate(descriptor.ModuleCodes);
            var owners = descriptors.Where(source => source.Id == descriptor.DisclosureSourceId).ToArray();
            if (string.IsNullOrWhiteSpace(descriptor.DisclosureSourceId) || owners.Length != 1
                || owners[0].ModuleId != descriptor.ModuleId)
                throw new AgentContributionException($"Agent tool '{descriptor.Name}' requires one installed disclosure source '{descriptor.DisclosureSourceId ?? "(missing)"}' owned by module '{descriptor.ModuleId}'.");
        }
        foreach (var tool in installed)
            domainEvent.Tools.Add(tool);
        return Task.CompletedTask;
    }
}
