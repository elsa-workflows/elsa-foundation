using System.Text.Json;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Events.Core.Contracts;

namespace Elsa.Agent.Services;

internal sealed class DefaultAgentPolicyEvaluator : IAgentPolicyEvaluator
{
    public ValueTask<AgentPolicyDecision> EvaluateAvailabilityAsync(AgentPolicy policy, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentPolicyDecision(policy.Enabled, policy.Enabled ? [] : [new("agent.disabled", "Assistant features are disabled by host policy.")]));

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
                violations.Add(new("agent.context.kind_denied", "This admitted context kind is disabled by host policy.", attachment.Id));
            var sensitivity = attachment.Sensitivity == AgentContextSensitivity.SecretRedacted ? AgentContextSensitivity.Sensitive : attachment.Sensitivity;
            if (sensitivity > policy.MaxContextSensitivity)
                violations.Add(new("agent.context.sensitivity_denied", "This attachment exceeds the host sensitivity ceiling.", attachment.Id));
        }
        return ValueTask.FromResult(new AgentPolicyDecision(violations.Count == 0, violations));
    }

    public ValueTask<AgentPolicyDecision> EvaluateCapabilityAsync(AgentPolicy policy, string capabilityId, CancellationToken cancellationToken = default)
    {
        var denied = policy.DeniedCapabilityIds.Contains(capabilityId, StringComparer.OrdinalIgnoreCase);
        return ValueTask.FromResult(new AgentPolicyDecision(!denied, denied ? [new("agent.capability.denied", "This capability is disabled by host policy.", capabilityId)] : []));
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
            return AgentResult<AgentContextContribution>.Failure("agent.disabled", "Assistant features are disabled by host policy.", 403);

        if (request.Disclosure is { } disclosure)
        {
            // A provider SDK may retain the old transcript. Omitting an obsolete snapshot from
            // today's projection cannot establish that the old disclosure is still permitted.
            var original = await policyEvaluator.EvaluateContextAsync(policy, disclosure.Context, cancellationToken);
            if (!original.Allowed)
            {
                var denied = original.Violations.Select(x => new AgentContributionDiagnostic("host", x.Code, x.Message, true)).ToArray();
                return new(new([], denied), new("agent.context.incomplete", "Current host policy withholds retained resource evidence.", 403));
            }
        }

        var collecting = new AgentContextCollecting(request);
        await events.Publish(collecting, cancellationToken);
        var diagnostics = collecting.Diagnostics.ToList();
        var sanitized = await sanitizer.SanitizeAsync(collecting.Attachments.ToList(), cancellationToken);
        foreach (var attachment in sanitized.Where(x => x.Sensitivity == AgentContextSensitivity.SecretRedacted))
            diagnostics.Add(new(attachment.ModuleId, "agent.context.redacted", "Secret context was redacted before provider use.", false));
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
                diagnostics.Add(new("host", "agent.context.budget_exceeded", "The module context exceeds the supported candidate budget; use a bounded contribution or authorized detail tool.", true));
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
        return requiredFailures.Count == 0
            ? AgentResult<AgentContextContribution>.Success(contribution)
            : new(contribution, new("agent.context.incomplete", string.Join(" ", requiredFailures.Select(x => x.Message)), 400));
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
