using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed partial class TriggerManagement
{
    internal async ValueTask<TriggerPreview> PreviewAsync(string id, JsonElement request, CancellationToken ct)
    {
        if (IsDeliveryControl(request.GetProperty("command")))
            return await RequireDeliveryControls().PreviewAsync(id, request, ct);
        var aggregate = await RequireResourceAsync(id, ct);
        if (Number(request, "expectedRevision") != aggregate.Trigger.Revision) throw Conflict();
        var command = request.GetProperty("command");
        var fingerprint = Fingerprint(command);
        var impact = new List<TriggerImpact>(); var findings = new List<TriggerFinding>();
        try
        {
            var proposed = await PrepareAsync(aggregate, command, ct);
            impact.Add(new("Trigger state", aggregate.Trigger.DesiredState.ToString(), proposed.Trigger.DesiredState.ToString()));
            impact.Add(new("Associations", aggregate.Subscriptions.Count.ToString(), proposed.Subscriptions.Count.ToString()));
            AddConfigurationImpact(aggregate.Trigger, proposed.Trigger, impact);
            AddGrantImpact(aggregate, proposed, impact);
            await AddScheduleImpactAsync(aggregate, proposed, impact, ct);
            foreach (var row in aggregate.Subscriptions.Concat(proposed.Subscriptions).DistinctBy(row => row.Id))
            {
                impact.Add(await CanReadTargetAsync(row.DefinitionId, row.SlotName, ct)
                    ? new("Target", Description: $"{row.DefinitionId} / {row.SlotName}")
                    : new("Restricted workflow", Description: "The resource change preserves restricted workflow details."));
            }
            impact.Add(new("Accepted work", Description: "Existing accepted deliveries and executions remain retained. This change does not cancel them."));
        }
        catch (TriggerOperationException exception) when (exception.Code is "validation" or "unavailable" or "route-conflict")
        { findings.Add(new(exception.Code, exception.Message)); }
        var observations = await ObserveAsync(aggregate, command, ct);
        var expires = time.GetUtcNow().AddMinutes(5);
        var proof = new TriggerPreviewProof(access.Subject, access.Scope, id, aggregate.Trigger.Revision, fingerprint, observations, expires);
        var token = previewProtector.Protect(JsonSerializer.Serialize(proof, StandaloneTriggersApi.Json));
        return new(token, expires, aggregate.Trigger.Revision, fingerprint, findings.Count == 0, impact, findings);
    }

    internal async ValueTask<TriggerChangeResult> ApplyAsync(string id, JsonElement request, CancellationToken ct)
    {
        if (IsDeliveryControl(request.GetProperty("command")))
            return await RequireDeliveryControls().ApplyAsync(id, request, ct);
        var command = request.GetProperty("command"); var requestId = RequestId(request);
        var fingerprint = Fingerprint(new { Operation = "ApplyTriggerChange", TriggerId = id, ExpectedRevision = Number(request, "expectedRevision"), Command = command });
        var replay = await store.FindMutationAsync(access.Scope, access.Subject, requestId, ct);
        if (replay is not null)
        {
            EnsureReplay(replay, fingerprint);
            var retained = await store.FindAsync(access.Scope, id, ct) ?? throw Missing();
            if (replay.TriggerId != id || !await access.CanReadAsync(retained.Trigger, ct)) throw Missing();
            await AuthorizeCommandAsync(retained.Trigger, command, ct);
            return new(requestId, "AlreadyApplied", replay.Revision, null);
        }
        var aggregate = await RequireResourceAsync(id, ct);
        TriggerPreviewProof proof;
        try { proof = JsonSerializer.Deserialize<TriggerPreviewProof>(previewProtector.Unprotect(Text(request, "previewId")), StandaloneTriggersApi.Json) ?? throw Conflict(); }
        catch (CryptographicException) { throw Conflict(); }
        catch (JsonException) { throw Conflict(); }
        if (proof.Actor != access.Subject || proof.Scope != access.Scope || proof.TriggerId != id || proof.Revision != aggregate.Trigger.Revision ||
            proof.Revision != Number(request, "expectedRevision") || proof.CommandFingerprint != Fingerprint(command) || proof.ExpiresAt <= time.GetUtcNow() ||
            proof.ObservationFingerprint != await ObserveAsync(aggregate, command, ct)) throw Conflict();
        var prepared = await PrepareAsync(aggregate, command, ct);
        var observedTargets = await ObserveTargetsAsync(aggregate, command, ct);
        if (proof.ObservationFingerprint != Fingerprint(new { Aggregate = aggregate, Targets = observedTargets })) throw Conflict();
        var receipt = await committer.CommitAsync(access.Scope, new(requestId, fingerprint, access.Subject, aggregate.Trigger.Revision,
            prepared.Trigger, prepared.Subscriptions) { ObservedTargets = observedTargets }, ct);
        return new(requestId, "Applied", receipt.Revision, await ViewAsync(prepared, ct), Effects:
            [new { message = "The reviewed resource change was committed. Accepted deliveries were not cancelled." }]);
    }

    private async ValueTask<TriggerAggregate> PrepareAsync(TriggerAggregate aggregate, JsonElement command, CancellationToken ct)
    {
        var trigger = aggregate.Trigger; var kind = Text(command, "kind");
        await AuthorizeCommandAsync(trigger, command, ct);
        if (kind is "EnableTrigger" or "EnableSubscription" && !committer.CanActivateType(trigger.TypeKey, trigger.SchemaVersion))
            throw new TriggerOperationException("unavailable", "This type's protected runtime source is not installed.");
        var rows = aggregate.Subscriptions.ToList(); var name = trigger.Name; var description = trigger.Description;
        var configuration = trigger.Configuration; var desired = trigger.DesiredState;
        var configurationRevision = trigger.ConfigurationRevision; var associationRevision = trigger.AssociationRevision;
        if (kind is "SaveConfiguration" or "DeleteTrigger" && trigger.Origin.ConfigurationReadOnly)
            throw Validation("This configuration is managed by its source.");
        if (kind == "SaveConfiguration")
        {
            name = Text(command, "name"); description = OptionalText(command, "description"); configuration = Configuration(command);
            await ValidateConfigurationAsync(trigger.TypeKey, trigger.SchemaVersion, configuration, ct);
            if (Fingerprint(configuration) != Fingerprint(trigger.Configuration))
            {
                configurationRevision++;
                // Grants scope the immutable source type, target and mappings, not configuration revisions.
                // Those scopes are unchanged here; explicit revocation and subscription edits own grant changes.
            }
        }
        else if (kind is "AddSubscription" or "SaveSubscription")
        {
            var old = kind == "SaveSubscription" ? FindSubscription(rows, command) : null;
            if (old?.Origin.ConfigurationReadOnly == true) throw Validation("This association is managed by its source.");
            var selector = Selector(command.GetProperty("target"));
            await RequireTargetReadAsync(selector.DefinitionId, selector.SlotName, ct);
            await access.RequireAsync("workflows/runtime:execute", new TriggerTargetResource(selector.DefinitionId, selector.SlotName), ct);
            var target = await targets.DescribeAsync(access.Scope, selector.DefinitionId, selector.SlotName, ct) ?? throw Validation("Choose a known authorized workflow.");
            var mappings = ReadMappings(command.GetProperty("mappings")); var enabled = Flag(command, "enabled");
            var subscriptionId = old?.Id ?? "subscription-" + Fingerprint(new { trigger.Id, trigger.Revision, Command = command })[..32];
            if (rows.Any(row => row.Id != old?.Id && row.DefinitionId == selector.DefinitionId && row.SlotName == selector.SlotName))
                throw new TriggerOperationException("conflict", "This trigger already has an association for the selected workflow slot.");
            var changed = old is null || old.DefinitionId != target.DefinitionId || old.SlotName != target.SlotName || old.ModelKey != target.ModelKey || Fingerprint(old.Mappings.Select(MappingJson)) != Fingerprint(mappings.Select(MappingJson));
            var grant = changed ? null : old?.Grant;
            if (OptionalText(command, "invocationRequest") == "Grant")
            {
                await access.RequireAsync(TriggerPermissions.Authorize, trigger, ct);
                grant = NewGrant(trigger, subscriptionId, target, old?.Grant);
            }
            if (enabled != old?.Enabled && (enabled || old is not null)) await access.RequireAsync(TriggerPermissions.Operate, trigger, ct);
            if (target.InputContract is { } contract)
            {
                var validation = mapper.ValidateMappings(contract, mappings);
                if (!validation.IsValid) throw new TriggerOperationException("validation", "Correct the association's input mappings.",
                    validation.Findings.GroupBy(finding => finding.InputName ?? "mappings").ToDictionary(group => group.Key, group => string.Join(" ", group.Select(finding => finding.Message))));
            }
            if (enabled && (target.State != TriggerTargetState.Available || target.InputContract is null || grant?.Status != TriggerGrantStatus.Granted))
                throw Validation("An enabled association requires an actual serving contract, valid mappings and an authorized invocation grant.");
            if (old is not null) rows.Remove(old);
            rows.Add(new(subscriptionId, trigger.Id, target.DefinitionId, target.SlotName, target.ModelKey, (old?.Revision ?? 0) + 1,
                (old?.MappingRevision ?? 0) + (changed ? 1 : 0), enabled, mappings, old?.Origin ?? new(TriggerManagementKind.Studio), grant));
            associationRevision++;
        }
        else if (kind is "RemoveSubscription" or "EnableSubscription" or "PauseSubscription" or "GrantInvocation" or "RevokeInvocation")
        {
            var row = FindSubscription(rows, command);
            if (row.Origin.ConfigurationReadOnly && kind is "RemoveSubscription" or "GrantInvocation" or "RevokeInvocation")
                throw Validation("This association's configuration and authority are managed by its source.");
            if (kind == "RemoveSubscription") rows.Remove(row);
            else
            {
                var grant = row.Grant; var enabled = row.Enabled;
                if (kind is "GrantInvocation" or "RevokeInvocation")
                {
                    var expectedGrant = command.TryGetProperty("expectedGrantRevision", out var property) && property.ValueKind != JsonValueKind.Null ? property.GetInt64() : (long?)null;
                    if (expectedGrant != grant?.Revision) throw Conflict();
                    if (kind == "GrantInvocation")
                    {
                        await RequireTargetReadAsync(row.DefinitionId, row.SlotName, ct);
                        await access.RequireAsync("workflows/runtime:execute", new TriggerTargetResource(row.DefinitionId, row.SlotName), ct);
                        var target = await targets.DescribeAsync(access.Scope, row.DefinitionId, row.SlotName, ct) ?? throw Missing();
                        grant = NewGrant(trigger, row.Id, target, grant);
                    }
                    else grant = Revoke(grant);
                }
                else enabled = kind == "EnableSubscription";
                if (enabled && kind == "EnableSubscription")
                {
                    if (!await IsEligibleAsync(row, ct)) throw Validation("The association is not currently eligible.");
                }
                rows[rows.IndexOf(row)] = CopySubscription(row, enabled, grant, true);
            }
            associationRevision++;
        }
        else if (kind == "EnableTrigger")
        {
            var eligible = false;
            foreach (var row in rows.Where(row => row.Enabled))
                eligible |= await IsEligibleAsync(row, ct);
            if (!eligible) throw Validation("Enable at least one eligible subscription first.");
            desired = TriggerDesiredState.Enabled;
        }
        else if (kind == "PauseTrigger") desired = TriggerDesiredState.Paused;
        else if (kind == "DeleteTrigger") desired = TriggerDesiredState.Deleted;
        else if (kind is "RetryRecipients" or "CancelPendingRecipients")
            throw new TriggerOperationException("unavailable", "Protected delivery controls are not available in this composition.");
        else throw Validation("The trigger change kind is unsupported.");
        if (rows.Count == 0 && desired == TriggerDesiredState.Enabled) desired = TriggerDesiredState.Paused;
        var replacement = new StandaloneTrigger(trigger.Id, name, trigger.TypeKey, trigger.SchemaVersion, configuration,
            trigger.Revision + 1, configurationRevision, associationRevision, desired, trigger.Origin, trigger.CreatedAt,
            time.GetUtcNow(), trigger.CreatedBy, description, trigger.HasActivated, trigger.TenantId);
        var result = new TriggerAggregate(replacement, rows);
        if (!committer.CanActivateType(trigger.TypeKey, trigger.SchemaVersion) &&
            (trigger.HasActivated || trigger.DesiredState == TriggerDesiredState.Enabled || desired == TriggerDesiredState.Enabled || rows.Any(row => row.Enabled)) &&
            !(committer.CanStopType(trigger.TypeKey, trigger.SchemaVersion) && TriggerStopOnlyChange.IsSafe(aggregate, result)))
            throw new TriggerOperationException("unavailable", "The source's protected activation or retained projection planner is unavailable.");
        await ValidateReservationChangeAsync(aggregate, result, ct);
        return result;
    }

    private void AddConfigurationImpact(StandaloneTrigger current, StandaloneTrigger proposed, List<TriggerImpact> impact)
    {
        if (current.Name != proposed.Name) impact.Add(new("Name", current.Name, proposed.Name));
        if (current.Description != proposed.Description) impact.Add(new("Description", current.Description, proposed.Description));
        if (current.ConfigurationRevision == proposed.ConfigurationRevision) return;
        var descriptor = types.Find(current.TypeKey);
        var fields = (descriptor?.ConfigurationFields ?? []).GroupBy(field => field.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        if (current.TypeKey == "http")
        {
            fields.TryAdd("http.mode", new("http.mode", "HTTP response mode"));
            fields.TryAdd("http.admissionPolicy", new("http.admissionPolicy", "HTTP admission policy"));
        }
        foreach (var field in fields.Values)
        {
            var before = ConfigurationValue(current.Configuration, field.Key);
            var after = ConfigurationValue(proposed.Configuration, field.Key);
            if (SameConfigurationValue(before, after)) continue;
            var sensitive = fields.Values.Any(candidate => candidate.Sensitive &&
                (candidate.Key == field.Key || field.Key.StartsWith(candidate.Key + ".", StringComparison.Ordinal)));
            impact.Add(new(field.Label, ConfigurationValueText(before, sensitive), ConfigurationValueText(after, sensitive)));
        }
        var declared = fields.Keys.Select(key => key.Split('.')[0]).ToHashSet(StringComparer.Ordinal);
        if (current.Configuration.EnumerateObject().Concat(proposed.Configuration.EnumerateObject())
            .Select(property => property.Name).Distinct(StringComparer.Ordinal).Any(key => !declared.Contains(key) &&
                !SameConfigurationValue(ConfigurationValue(current.Configuration, key), ConfigurationValue(proposed.Configuration, key))))
            impact.Add(new("Other configuration", Description: "Additional configuration values changed; values without declared display metadata are not included in this preview."));
    }

    private static JsonElement? ConfigurationValue(JsonElement configuration, string key)
    {
        if (configuration.TryGetProperty(key, out var direct)) return direct;
        var value = configuration;
        foreach (var segment in key.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out var child)) return null;
            value = child;
        }
        return value;
    }

    private static bool SameConfigurationValue(JsonElement? before, JsonElement? after) =>
        before is { } value ? after is { } other && JsonElement.DeepEquals(value, other) : after is null;

    private static string ConfigurationValueText(JsonElement? value, bool sensitive) => value is not { } configured
        ? "Not configured" : sensitive ? "[Protected]" : configured.ValueKind switch
        {
            JsonValueKind.String => configured.GetString()!,
            JsonValueKind.Object or JsonValueKind.Array => "[Structured value]",
            _ => configured.GetRawText()
        };

    private static void AddGrantImpact(TriggerAggregate current, TriggerAggregate proposed, List<TriggerImpact> impact)
    {
        var retained = 0; var granted = 0; var revoked = 0; var cleared = 0; var removed = 0;
        foreach (var id in current.Subscriptions.Concat(proposed.Subscriptions).Select(row => row.Id).Distinct(StringComparer.Ordinal))
        {
            var old = current.Subscriptions.SingleOrDefault(row => row.Id == id);
            var next = proposed.Subscriptions.SingleOrDefault(row => row.Id == id);
            if (old?.Grant is null && next?.Grant is null) continue;
            if (next is null) removed++;
            else if (Fingerprint(old?.Grant) == Fingerprint(next.Grant)) retained++;
            else if (next.Grant is null) cleared++;
            else if (next.Grant.Status == TriggerGrantStatus.Revoked) revoked++;
            else granted++;
        }
        impact.Add(new("Invocation grants", GrantSummary(current), GrantSummary(proposed),
            $"Retained: {retained}; granted or replaced: {granted}; revoked: {revoked}; cleared: {cleared}; removed with association: {removed}. " +
            "Changes apply to fresh admissions. Accepted work keeps its captured authority."));
    }

    private static string GrantSummary(TriggerAggregate aggregate) =>
        $"{aggregate.Subscriptions.Count(row => row.Grant?.Status == TriggerGrantStatus.Granted)} granted, " +
        $"{aggregate.Subscriptions.Count(row => row.Grant?.Status == TriggerGrantStatus.Revoked)} revoked, " +
        $"{aggregate.Subscriptions.Count(row => row.Grant is null)} without a grant";

    private async ValueTask AuthorizeCommandAsync(StandaloneTrigger trigger, JsonElement command, CancellationToken ct)
    {
        var permission = Text(command, "kind") switch
        {
            "EnableTrigger" or "PauseTrigger" or "EnableSubscription" or "PauseSubscription" => TriggerPermissions.Operate,
            "GrantInvocation" or "RevokeInvocation" => TriggerPermissions.Authorize,
            "RetryRecipients" => TriggerPermissions.Retry,
            "CancelPendingRecipients" => TriggerPermissions.Cancel,
            "SaveConfiguration" or "AddSubscription" or "SaveSubscription" or "RemoveSubscription" or "DeleteTrigger" => TriggerPermissions.Manage,
            _ => throw Validation("The trigger change kind is unsupported.")
        };
        await access.RequireAsync(permission, trigger, ct);
    }

    private async ValueTask<string> ObserveAsync(TriggerAggregate aggregate, JsonElement command, CancellationToken ct)
        => Fingerprint(new { Aggregate = aggregate, Targets = await ObserveTargetsAsync(aggregate, command, ct) });

    private async ValueTask<IReadOnlyList<TriggerTargetObservation>> ObserveTargetsAsync(TriggerAggregate aggregate, JsonElement command, CancellationToken ct)
    {
        var selectors = aggregate.Subscriptions.Select(row => new TriggerTargetSelector(row.DefinitionId, row.SlotName)).ToList();
        if (command.TryGetProperty("target", out var target)) selectors.Add(Selector(target));
        var observations = new List<TriggerTargetObservation>();
        foreach (var selector in selectors.Distinct())
        {
            var description = await targets.DescribeAsync(access.Scope, selector.DefinitionId, selector.SlotName, ct);
            observations.Add(new(selector.DefinitionId, selector.SlotName, description?.SlotRevision, description?.PublicationId,
                description?.ContractRevision, description?.ModelKey, description?.State ?? TriggerTargetState.Unavailable));
        }
        return observations.AsReadOnly();
    }

    private static bool IsDeliveryControl(JsonElement command) => Text(command, "kind") is "RetryRecipients" or "CancelPendingRecipients";
    private TriggerDeliveryOperations RequireDeliveryControls() => deliveries?.SupportsControls == true ? deliveries :
        throw new TriggerOperationException("unavailable", "Protected delivery controls are not available in this composition.");

    private TriggerInvocationGrant NewGrant(StandaloneTrigger trigger, string subscriptionId, TriggerTargetDescription target, TriggerInvocationGrant? old) =>
        new(old?.Id ?? "grant-" + subscriptionId, trigger.Id, subscriptionId, target.DefinitionId, target.SlotName, target.ModelKey,
            trigger.TypeKey, (old?.Revision ?? 0) + 1, TriggerGrantStatus.Granted, access.Subject, time.GetUtcNow());
    private TriggerInvocationGrant? Revoke(TriggerInvocationGrant? grant) => grant is null || grant.Status == TriggerGrantStatus.Revoked ? grant :
        new(grant.Id, grant.TriggerId, grant.SubscriptionId, grant.DefinitionId, grant.SlotName, grant.ModelKey, grant.SourceTypeKey,
            grant.Revision + 1, TriggerGrantStatus.Revoked, grant.GrantedBy, grant.GrantedAt, access.Subject, time.GetUtcNow());
    private static TriggerSubscription CopySubscription(TriggerSubscription row, bool? enabled = null, TriggerInvocationGrant? grant = null, bool replaceGrant = false) =>
        new(row.Id, row.TriggerId, row.DefinitionId, row.SlotName, row.ModelKey, row.Revision + 1, row.MappingRevision,
            enabled ?? row.Enabled, row.Mappings, row.Origin, replaceGrant ? grant : row.Grant);
    private static TriggerSubscription FindSubscription(List<TriggerSubscription> rows, JsonElement command)
    { var row = rows.SingleOrDefault(row => row.Id == Text(command, "subscriptionId")) ?? throw Missing(); if (Number(command, "expectedSubscriptionRevision") != row.Revision) throw Conflict(); return row; }

    internal static IReadOnlyList<TriggerInputMapping> ReadMappings(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) throw Validation("Mappings must be a list.");
        var mappings = array.EnumerateArray().Select(item => new TriggerInputMapping(Text(item, "inputName"), Binding(item.GetProperty("binding")))).ToArray();
        if (mappings.Select(mapping => mapping.InputName).Distinct(StringComparer.Ordinal).Count() != mappings.Length) throw Validation("Mapped input names must be distinct.");
        return mappings;
    }
    private static TriggerValueBinding Binding(JsonElement value) => Text(value, "kind") switch
    {
        "Literal" => new TriggerLiteralBinding(value.GetProperty("value")),
        "SourceField" => new TriggerSourceBinding(value.GetProperty("path").EnumerateArray().Select(item => item.GetString() ?? throw Validation("Source paths contain string segments.")).ToArray()),
        "Expression" => Expression(value),
        _ => throw Validation("The input binding kind is unsupported.")
    };
    private static TriggerExpressionBinding Expression(JsonElement value)
    {
        var template = value.GetProperty("template");
        var resultType = JsonSerializer.Deserialize<TypeReference>(template.GetProperty("resultType"), StandaloneTriggersApi.Json) ?? throw Validation("Choose an expression result type.");
        return new(Text(template, "language"), Text(template, "source"), resultType, template.GetProperty("options"), Text(template, "capabilityProfile"),
            value.GetProperty("parameters").EnumerateObject().ToDictionary(item => item.Name, item => Binding(item.Value), StringComparer.Ordinal));
    }
    internal static JsonElement MappingJson(TriggerInputMapping mapping)
    {
        object binding = mapping.Binding switch
        {
            TriggerLiteralBinding literal => new { kind = "Literal", value = literal.Value },
            TriggerSourceBinding source => new { kind = "SourceField", path = source.Path },
            TriggerExpressionBinding expression => new { kind = "Expression", template = new { expression.Language, expression.Source,
                expression.ResultType, expression.Options, expression.CapabilityProfile }, parameters = expression.Parameters.ToDictionary(pair => pair.Key,
                    pair => JsonSerializer.SerializeToElement(pair.Value, StandaloneTriggersApi.Json), StringComparer.Ordinal) },
            _ => throw Validation("The stored input binding kind is unsupported.")
        };
        return JsonSerializer.SerializeToElement(new { mapping.InputName, binding }, StandaloneTriggersApi.Json);
    }
}
