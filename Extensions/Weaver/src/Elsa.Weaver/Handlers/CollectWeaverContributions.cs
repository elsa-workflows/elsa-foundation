using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Elsa.Weaver.Handlers;

/// <summary>The single Sequential context aggregator; modules register sources rather than event handlers.</summary>
internal sealed class CollectWeaverContext(IEnumerable<IWeaverContextProvider> sources, IOptions<WeaverContextOptions> options,
    TimeProvider timeProvider) : IEventHandler<WeaverContextCollecting>
{
    public async Task Handle(WeaverContextCollecting domainEvent, CancellationToken cancellationToken)
    {
        var request = domainEvent.Request;
        var callerIds = new HashSet<string>(StringComparer.Ordinal);
        if (request.CallerAttachments.Any(attachment => !callerIds.Add(attachment.Id)))
        {
            domainEvent.Diagnostics.Add(new("host", "weaver.context.duplicate_id", "Caller context attachment IDs must be unique within one prompt.", true));
            return;
        }

        var registered = sources.ToList();
        var duplicate = registered.GroupBy(x => x.Descriptor.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new WeaverContributionException($"Duplicate Weaver context source '{duplicate.Key}'.");

        if (request.Disclosure is { } disclosure)
        {
            await AdmitDisclosureAsync(registered, request, disclosure, domainEvent, cancellationToken);
            return;
        }

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in registered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = source.Descriptor;
            var caller = request.CallerAttachments.Where(x => x.ModuleId == descriptor.ModuleId && descriptor.ContextKinds.Contains(x.ContentType, StringComparer.Ordinal)).ToList();
            var applicable = descriptor.ScopeKind == "*" || caller.Count > 0 || descriptor.ResourceTypes.Contains(request.Target?.ResourceType ?? string.Empty, StringComparer.Ordinal)
                || (!string.IsNullOrEmpty(request.ScopeKind) && string.Equals(descriptor.ScopeKind, request.ScopeKind, StringComparison.Ordinal));
            if (!applicable)
                continue;
            var admittedInputs = new List<WeaverContextAttachment>();
            foreach (var attachment in caller)
            {
                claimed.Add(attachment.Id);
                if (!descriptor.SchemaVersions.Contains(attachment.SchemaVersion, StringComparer.Ordinal))
                    domainEvent.Diagnostics.Add(new(descriptor.Id, "weaver.context.version_unsupported", "The installed module does not support this context schema version.", true));
                else
                    admittedInputs.Add(attachment with
                    {
                        Origin = WeaverContextOrigin.ClientSnapshot,
                        AgentScopes = [],
                        // A caller's redaction claim is not evidence that its body is redacted.
                        Sensitivity = attachment.Sensitivity == WeaverContextSensitivity.SecretRedacted ? WeaverContextSensitivity.Secret : attachment.Sensitivity
                    });
            }
            var contribution = await CollectSourceAsync(source, request with { CallerAttachments = admittedInputs }, domainEvent.Diagnostics, cancellationToken);
            if (contribution is null)
                continue;
            foreach (var diagnostic in contribution.Diagnostics)
                Add(domainEvent, OwnedMarker(diagnostic, descriptor.Id));
            foreach (var attachment in contribution.Attachments)
            {
                if (attachment.ModuleId != descriptor.ModuleId)
                    throw new WeaverContributionException($"Context source '{descriptor.Id}' returned another module's context.");
                domainEvent.Attachments.Add(Snapshot(attachment) with { ContributorId = descriptor.Id });
            }
        }
        foreach (var attachment in request.CallerAttachments.Where(x => !claimed.Contains(x.Id)))
            domainEvent.Diagnostics.Add(new(attachment.ModuleId, "weaver.context.kind_unsupported", "No installed module admits the supplied context kind and owner.", true));
        var duplicateAttachment = domainEvent.Attachments.GroupBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicateAttachment is not null)
            throw new WeaverContributionException($"Duplicate admitted context attachment '{duplicateAttachment.Key}'.");
    }

    private async Task AdmitDisclosureAsync(IReadOnlyList<IWeaverContextProvider> registered, WeaverContextRequest request,
        WeaverContextDisclosureRequest disclosure, WeaverContextCollecting domainEvent, CancellationToken cancellationToken)
    {
        var sourceIds = disclosure.Context.Select(x => x.ContributorId)
            .Concat(disclosure.Results.Select(x => x.Access?.DisclosureSourceId)).Distinct(StringComparer.Ordinal);
        foreach (var sourceId in sourceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = registered.SingleOrDefault(x => x.Descriptor.Id == sourceId);
            var ownedContext = disclosure.Context.Where(x => x.ContributorId == sourceId).ToArray();
            var ownedResults = disclosure.Results.Where(x => x.Access?.DisclosureSourceId == sourceId).ToArray();
            if (source is null || ownedContext.Any(x => x.ModuleId != source.Descriptor.ModuleId
                    || !source.Descriptor.ContextKinds.Contains(x.ContentType, StringComparer.Ordinal)
                    || !source.Descriptor.SchemaVersions.Contains(x.SchemaVersion, StringComparer.Ordinal))
                || ownedResults.Any(x => x.Access?.ModuleId != source.Descriptor.ModuleId))
            {
                domainEvent.Diagnostics.Add(new("host", "weaver.context.disclosure_unavailable",
                    "The original installed context source, owner or schema is unavailable. Start a fresh request with current modules.", true));
                continue;
            }
            var owned = disclosure with
            {
                Context = ownedContext.Select(Snapshot).ToArray(),
                Results = ownedResults
            };
            var contribution = await CollectSourceAsync(source, request with { CallerAttachments = [], Disclosure = owned },
                domainEvent.Diagnostics, cancellationToken);
            if (contribution is null) continue;
            foreach (var diagnostic in contribution.Diagnostics) Add(domainEvent, OwnedMarker(diagnostic, source.Descriptor.Id));
            foreach (var attachment in contribution.Attachments)
            {
                if (attachment.ModuleId != source.Descriptor.ModuleId
                    || !source.Descriptor.ContextKinds.Contains(attachment.ContentType, StringComparer.Ordinal)
                    || !source.Descriptor.SchemaVersions.Contains(attachment.SchemaVersion, StringComparer.Ordinal))
                    throw new WeaverContributionException($"Context source '{source.Descriptor.Id}' returned an invalid disclosure projection.");
                domainEvent.Attachments.Add(Snapshot(attachment) with { ContributorId = source.Descriptor.Id });
            }
        }
        if (domainEvent.Attachments.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new WeaverContributionException("Retained context disclosure produced duplicate attachment identities.");
    }

    private async Task<WeaverContextContribution?> CollectSourceAsync(IWeaverContextProvider source, WeaverContextRequest request,
        ICollection<WeaverContributionDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(options.Value.SourceTimeout, timeProvider);
        using var sourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        Task<WeaverContextContribution>? pending = null;
        try
        {
            pending = request.Disclosure is { } disclosure
                ? source.AdmitDisclosureAsync(disclosure, sourceCancellation.Token).AsTask()
                : source.CollectAsync(request, sourceCancellation.Token).AsTask();
            var contribution = await pending.WaitAsync(sourceCancellation.Token);
            sourceCancellation.Token.ThrowIfCancellationRequested();
            return contribution;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            diagnostics.Add(new(source.Descriptor.Id, "weaver.context.source_timed_out",
                "The installed context source did not complete within the host's collection bound.", request.Disclosure is not null || source.Descriptor.Required));
            return null;
        }
        catch (Exception error) when (request.Disclosure is null && !source.Descriptor.Required && error is not WeaverContributionException)
        {
            diagnostics.Add(new(source.Descriptor.Id, "weaver.context.source_failed",
                "Optional installed context is unavailable because its source failed.", false));
            return null;
        }
        catch (Exception error)
        {
            throw new WeaverContributionException($"Required context source '{source.Descriptor.Id}' failed.", error);
        }
        finally
        {
            if (pending is not null)
                _ = ObserveLateCompletionAsync(pending);
        }
    }

    private static async Task ObserveLateCompletionAsync(Task<WeaverContextContribution> pending)
    {
        // A source may ignore cancellation. Its late result never rejoins admission; observe faults only.
        try { await pending; }
        catch (Exception) { }
    }

    // Only this handler records markers, and only for a source's own diagnostics: another event handler that adds a
    // marked diagnostic to the public collection is never recorded, so the collector clears its marker.
    private static void Add(WeaverContextCollecting domainEvent, WeaverContributionDiagnostic diagnostic)
    {
        domainEvent.Diagnostics.Add(diagnostic);
        if (diagnostic.OwnerUnavailable) Services.WeaverContextOwnerMarkers.Record(domainEvent, diagnostic);
    }

    // The owner-unavailable marker counts only on a diagnostic a source reports under its own descriptor id, so a source
    // cannot impersonate the engine's "host" diagnostics or another source to claim it.
    private static WeaverContributionDiagnostic OwnedMarker(WeaverContributionDiagnostic diagnostic, string sourceId) =>
        diagnostic.OwnerUnavailable && !string.Equals(diagnostic.ContributorId, sourceId, StringComparison.Ordinal)
            ? diagnostic with { OwnerUnavailable = false }
            : diagnostic;

    private static WeaverContextAttachment Snapshot(WeaverContextAttachment attachment)
    {
        try
        {
            return attachment with
            {
                Content = attachment.Content is null || attachment.Sensitivity == WeaverContextSensitivity.Secret
                    ? null : JsonSerializer.SerializeToElement(attachment.Content).Clone(),
                References = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(attachment.References.ToDictionary()),
                AgentScopes = Array.AsReadOnly(attachment.AgentScopes.ToArray())
            };
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new WeaverContributionException("A context source returned content that cannot be transported as JSON.", error);
        }
    }
}

/// <summary>The single Sequential capability aggregator over installed providers.</summary>
internal sealed class CollectWeaverCapabilities(IEnumerable<IWeaverCapabilityProvider> sources) : IEventHandler<WeaverCapabilitiesCollecting>
{
    public async Task Handle(WeaverCapabilitiesCollecting domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            foreach (var capability in await source.GetCapabilitiesAsync(cancellationToken))
                domainEvent.Capabilities.Add(capability);
        var duplicate = domainEvent.Capabilities.GroupBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new WeaverContributionException($"Duplicate Weaver capability '{duplicate.Key}'.");
    }
}

/// <summary>The single Sequential tool aggregator over the existing installed tool contract.</summary>
internal sealed class CollectWeaverTools(IEnumerable<IWeaverTool> tools, IEnumerable<IWeaverContextProvider> sources) : IEventHandler<WeaverToolsCollecting>
{
    public Task Handle(WeaverToolsCollecting domainEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = tools.ToArray();
        // Registration integrity uses only public metadata. Context/disclosure behavior remains
        // exclusively on WeaverContextCollecting and is rechecked under current authority there.
        var descriptors = sources.Select(source => source.Descriptor).ToArray();
        foreach (var tool in installed)
        {
            var descriptor = tool.Descriptor;
            var owners = descriptors.Where(source => source.Id == descriptor.DisclosureSourceId).ToArray();
            if (string.IsNullOrWhiteSpace(descriptor.DisclosureSourceId) || owners.Length != 1
                || owners[0].ModuleId != descriptor.ModuleId)
                throw new WeaverContributionException($"Weaver tool '{descriptor.Name}' requires one installed disclosure source '{descriptor.DisclosureSourceId ?? "(missing)"}' owned by module '{descriptor.ModuleId}'.");
        }
        foreach (var tool in installed)
            domainEvent.Tools.Add(tool);
        return Task.CompletedTask;
    }
}
