using System.Text.Json;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Core.Services;
using Elsa.Events.Core.Contracts;

namespace Elsa.Weaver.Services;

internal sealed class DefaultAgentPolicyEvaluator : IAgentPolicyEvaluator
{
    public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentPolicyDecision(policy.Enabled, policy.Enabled ? [] : [new("weaver.disabled", "Assistant features are disabled by host policy.")]));

    public ValueTask<AgentPolicyDecision> EvaluateContextAsync(AgentPolicy policy, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
    {
        var violations = new List<AgentViolation>();
        foreach (var attachment in attachments)
        {
            var kinds = policy.AllowedContextKinds;
            var normalized = attachment.ContentType.Replace('-', '.');
            if (!kinds.Contains("*", StringComparer.Ordinal) && !kinds.Contains(attachment.Source, StringComparer.OrdinalIgnoreCase)
                && !kinds.Contains(attachment.ContentType, StringComparer.OrdinalIgnoreCase) && !kinds.Contains(normalized, StringComparer.OrdinalIgnoreCase)
                && !kinds.Contains($"{attachment.Source}.{normalized}", StringComparer.OrdinalIgnoreCase))
                violations.Add(new("weaver.context.kind_denied", "This admitted context kind is disabled by host policy.", attachment.Id));
            var sensitivity = attachment.Sensitivity == AgentContextSensitivity.SecretRedacted ? AgentContextSensitivity.Sensitive : attachment.Sensitivity;
            if (sensitivity > policy.MaxContextSensitivity)
                violations.Add(new("weaver.context.sensitivity_denied", "This attachment exceeds the host sensitivity ceiling.", attachment.Id));
        }
        return ValueTask.FromResult(new AgentPolicyDecision(violations.Count == 0, violations));
    }

    public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default)
    {
        var denied = policy.DeniedCapabilityIds.Contains(capabilityId, StringComparer.OrdinalIgnoreCase);
        return ValueTask.FromResult(new AgentPolicyDecision(!denied, denied ? [new("weaver.capability.denied", "This capability is disabled by host policy.", capabilityId)] : []));
    }
}

internal sealed class DefaultAgentContextSanitizer : IAgentContextSanitizer
{
    public ValueTask<IReadOnlyCollection<AgentContextAttachment>> SanitizeAsync(IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<AgentContextAttachment>>(attachments.Select(x => x.Sensitivity == AgentContextSensitivity.Secret
            ? x with
            {
                Id = AgentProviderPrimitives.NewId(),
                Sensitivity = AgentContextSensitivity.SecretRedacted,
                Content = null,
                SourceId = null,
                Label = "Redacted context",
                Summary = "Secret context was redacted before provider use.",
                References = new Dictionary<string, string>(),
                Target = null,
                Client = null,
                AgentScopes = [],
                Scope = "redacted"
            }
            : x).ToList());
    }
}

internal sealed class DefaultAgentContextCollector(
    IInlineEventPublisher events,
    IAgentContextSanitizer sanitizer,
    IAgentPolicyEvaluator policyEvaluator,
    IAgentAuditSink auditSink) : IAgentContextCollector
{
    public async Task<AgentResult<AgentContextContribution>> CollectAsync(AgentPolicy policy, AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        var availability = await policyEvaluator.EvaluateAvailabilityAsync(policy, cancellationToken);
        if (!availability.Allowed)
            return AgentResult<AgentContextContribution>.Failure("weaver.disabled", "Assistant features are disabled by host policy.", 403);

        if (request.Disclosure is { } disclosure)
        {
            // A provider SDK may retain the old transcript. Omitting an obsolete snapshot from
            // today's projection cannot establish that the old disclosure is still permitted.
            var original = await policyEvaluator.EvaluateContextAsync(policy, disclosure.Context, cancellationToken);
            if (!original.Allowed)
            {
                var denied = original.Violations.Select(x => new AgentContributionDiagnostic("host", x.Code, x.Message, true)).ToArray();
                return new(new([], denied), new("weaver.context.incomplete", "Current host policy withholds retained resource evidence.", 403));
            }
        }

        var collecting = new AgentContextCollecting(request);
        await events.Publish(collecting, cancellationToken);
        // A marker counts only where CollectAgentContext recorded it for a source's own diagnostic; any other is cleared.
        var diagnostics = collecting.Diagnostics
            .Select(x => x.OwnerUnavailable && !AgentContextOwnerMarkers.IsRecorded(collecting, x) ? x with { OwnerUnavailable = false } : x)
            .ToList();
        var sanitized = await sanitizer.SanitizeAsync(collecting.Attachments.ToList(), cancellationToken);
        foreach (var attachment in sanitized.Where(x => x.Sensitivity == AgentContextSensitivity.SecretRedacted))
            diagnostics.Add(new(attachment.ModuleId, "weaver.context.redacted", "Secret context was redacted before provider use.", false));
        IReadOnlyCollection<AgentContextAttachment> attachments;
        try
        {
            attachments = sanitized.Select(x => x with
            {
                Content = x.Content is null ? null : JsonSerializer.SerializeToElement(x.Content).Clone(),
                References = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(x.References.ToDictionary()),
                AgentScopes = Array.AsReadOnly(x.AgentScopes.ToArray())
            }).ToList();
            if (attachments.Count > 32 || attachments.Any(x => x.Summary.Length > 4096 || JsonSerializer.Serialize(x.Content).Length > 32768))
                diagnostics.Add(new("host", "weaver.context.budget_exceeded", "The module context exceeds the supported candidate budget; use a bounded contribution or authorized detail tool.", true));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new AgentContributionException("A context source returned content that cannot be transported as JSON.", ex);
        }
        var decision = await policyEvaluator.EvaluateContextAsync(policy, attachments, cancellationToken);
        foreach (var violation in decision.Violations)
            diagnostics.Add(new("host", violation.Code, violation.Message, true));
        var requiredFailures = diagnostics.Where(x => x.Required).ToList();
        var contribution = new AgentContextContribution(attachments, diagnostics);
        await auditSink.EmitAsync(new(
            AgentProviderPrimitives.NewId(),
            requiredFailures.Count == 0 ? AgentAuditEventKind.ContextCollected : AgentAuditEventKind.ContextDenied,
            request.SessionId, null,
            requiredFailures.Count == 0 ? "Context admitted through installed modules and host policy." : "Required context admission failed.",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["attachmentCount"] = attachments.Count.ToString(), ["diagnosticCount"] = diagnostics.Count.ToString() }), cancellationToken);
        if (requiredFailures.Count == 0)
            return AgentResult<AgentContextContribution>.Success(contribution);
        // An owner that could not decide stays a refusal, but reports as unavailable with its own stable code and the
        // engine's fixed message, never its own text. Only when every required failure says so: any definite refusal,
        // host diagnostic or reserved code keeps the ordinary incomplete-context failure exactly.
        if (requiredFailures.All(x => x.OwnerUnavailable && AgentContextOwnerMarkers.IsRecorded(collecting, x)))
            return new(contribution, AgentContextOwnerMarkers.Unavailable(requiredFailures[0].Code));
        return new(contribution, new("weaver.context.incomplete", string.Join(" ", requiredFailures.Select(x => x.Message)), 400));
    }
}

internal sealed class DefaultAgentCapabilityCatalog(IInlineEventPublisher events) : IAgentCapabilityCatalog
{
    public async Task<IReadOnlyCollection<AgentCapability>> ListAsync(CancellationToken cancellationToken = default)
    {
        var collecting = new AgentCapabilitiesCollecting();
        await events.Publish(collecting, cancellationToken);
        return collecting.Capabilities.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }
}
