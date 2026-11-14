using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed partial class TriggerManagement(IStandaloneTriggerStore store, ITriggerManagementCommitter committer,
    ITriggerRequestAccess access, IStandaloneTriggerTypeCatalog types, IStandaloneTriggerTargetDiscovery targets,
    ITriggerInputMapper mapper, IInlineEventPublisher events, IDataProtectionProvider protection, TimeProvider time,
    TriggerDeliveryOperations? deliveries = null, IRecurringTriggerScheduleStore? schedules = null,
    IRuntimeIngressReservationReader? reservations = null, IEnumerable<ITriggerReservationPlanner>? reservationPlanners = null,
    IEnumerable<IPortableExpressionHandler>? expressionHandlers = null, IPortableExpressionEvaluator? expressionEvaluator = null,
    IEnumerable<IStandaloneSchedulePlanner>? schedulePlanners = null)
{
    private readonly IDataProtector previewProtector = protection.CreateProtector("Elsa.StandaloneTriggers.ChangePreview.v1");

    internal async ValueTask<TriggerAggregate> RequireResourceAsync(string id, CancellationToken ct)
    {
        var aggregate = await store.FindAsync(access.Scope, id, ct);
        if (aggregate is null || aggregate.Trigger.DesiredState == TriggerDesiredState.Deleted || !await access.CanReadAsync(aggregate.Trigger, ct)) throw Missing();
        return aggregate;
    }

    internal async ValueTask<TriggerPage<TriggerView>> ListAsync(TriggerResourceQuery query, CancellationToken ct)
    {
        CheckLimit(query.Limit);
        var page = await store.ListAsync(access.Scope, query with { After = DecodeCursor(query.After) }, ct);
        var visible = new List<TriggerView>();
        foreach (var trigger in page.Items)
            if (await access.CanReadAsync(trigger, ct))
            {
                var aggregate = await store.FindAsync(access.Scope, trigger.Id, ct);
                if (aggregate is not null) visible.Add(await ViewAsync(aggregate, ct));
            }
        return new(visible, EncodeCursor(page.Next));
    }

    internal async ValueTask<TriggerView> ReadAsync(string id, CancellationToken ct) => await ViewAsync(await RequireResourceAsync(id, ct), ct);

    internal async ValueTask<TriggerPage<TriggerSubscriptionView>> SubscriptionsAsync(TriggerSubscriptionQuery query, CancellationToken ct)
    {
        CheckLimit(query.Limit);
        if ((query.TriggerId is null) == (query.DefinitionId is null)) throw Validation("Choose exactly one trigger or workflow.");
        if (query.TriggerId is not null) _ = await RequireResourceAsync(query.TriggerId, ct);
        else await RequireTargetReadAsync(query.DefinitionId!, query.SlotName ?? "default", ct);
        var page = await store.ListSubscriptionsAsync(access.Scope, query with { After = DecodeCursor(query.After) }, ct);
        var result = new List<TriggerSubscriptionView>();
        foreach (var subscription in page.Items)
        {
            var aggregate = await store.FindAsync(access.Scope, subscription.TriggerId, ct);
            if (aggregate is not null && await access.CanReadAsync(aggregate.Trigger, ct))
                result.Add(await SubscriptionViewAsync(aggregate.Trigger, subscription, ct));
        }
        return new(result, EncodeCursor(page.Next));
    }

    internal async ValueTask<TriggerSubscriptionView> SubscriptionAsync(string id, CancellationToken ct)
    {
        var subscription = await store.FindSubscriptionAsync(access.Scope, id, ct) ?? throw Missing();
        var resource = await RequireResourceAsync(subscription.TriggerId, ct);
        return await SubscriptionViewAsync(resource.Trigger, subscription, ct);
    }

    internal async ValueTask<TriggerTargetView> TargetAsync(string id, string slot, CancellationToken ct)
    {
        await RequireTargetReadAsync(id, slot, ct);
        return TriggerTargetView.From(await targets.DescribeAsync(access.Scope, id, slot, ct) ?? throw Missing());
    }

    internal async ValueTask<TriggerPage<TriggerTargetView>> TargetsAsync(TriggerTargetQuery query, CancellationToken ct)
    {
        CheckLimit(query.Limit);
        var page = await targets.ListAsync(access.Scope, query with { After = DecodeCursor(query.After) }, ct);
        var result = new List<TriggerTargetView>();
        foreach (var target in page.Items)
            if (await CanReadTargetAsync(target.DefinitionId, target.SlotName, ct)) result.Add(TriggerTargetView.From(target));
        return new(result, EncodeCursor(page.Next));
    }

    internal async ValueTask<TriggerPage<TriggerTypeView>> TypesAsync(CancellationToken ct)
    {
        var actor = await access.ActorAsync(ct);
        if (!actor.CanRead) throw Missing();
        return new(types.List().Select(type => new TriggerTypeView(type.TypeKey, type.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            type.DisplayName, type.Family, type.ConfigurationFields, actor.CanManage, type.Description)
            { ConfigurationEditorId = type.ConfigurationEditorId }).ToArray(), null);
    }

    internal async ValueTask<TriggerView> CreateAsync(JsonElement request, CancellationToken ct)
    {
        await access.RequireAsync(TriggerPermissions.Manage, null, ct);
        var requestId = RequestId(request); var fingerprint = Fingerprint(new { Operation = "CreateTrigger", Request = request });
        var replay = await store.FindMutationAsync(access.Scope, access.Subject, requestId, ct);
        if (replay is not null)
        {
            EnsureReplay(replay, fingerprint);
            return await ReadAsync(replay.TriggerId, ct);
        }
        var typeKey = Text(request, "typeKey"); var descriptor = types.Find(typeKey) ?? throw Validation("The trigger type is not installed.");
        var schemaVersion = SchemaVersion(request);
        if (schemaVersion != descriptor.SchemaVersion) throw Validation("The trigger schema is unsupported.");
        var configuration = Configuration(request);
        await ValidateConfigurationAsync(typeKey, schemaVersion, configuration, ct);
        var now = time.GetUtcNow(); var id = "trigger-" + Fingerprint(new { access.Scope, access.Subject, requestId })[..32];
        var trigger = new StandaloneTrigger(id, Text(request, "name"), typeKey, schemaVersion, configuration,
            1, 1, 0, TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), now, now, access.Subject,
            OptionalText(request, "description"), tenantId: access.Scope.TenantId);
        await access.RequireAsync(TriggerPermissions.Manage, trigger, ct);
        await committer.CommitAsync(access.Scope, new(requestId, fingerprint, access.Subject, null, trigger, []) { ObservedTargets = [] }, ct);
        return await ReadAsync(id, ct);
    }

    internal async ValueTask<object> MappingContextAsync(string id, JsonElement request, CancellationToken ct)
    {
        var aggregate = await RequireResourceAsync(id, ct);
        var selector = Selector(request.GetProperty("target"));
        var target = await TargetAsync(selector.DefinitionId, selector.SlotName, ct);
        var descriptor = types.Find(aggregate.Trigger.TypeKey);
        var languages = (expressionHandlers ?? []).Select(handler => handler.Language).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(language => language, StringComparer.OrdinalIgnoreCase)
            .Select(language => new { language, displayName = language, available = expressionEvaluator is not null,
                reason = expressionEvaluator is null ? "The portable expression evaluator is unavailable." : null }).ToArray();
        return new { sourceSchema = new { fields = descriptor?.SourceFields ?? [] }, target,
            supportedExpressions = languages, findings = Array.Empty<TriggerFinding>() };
    }

    internal async ValueTask<object> MappingPreviewAsync(string id, JsonElement request, CancellationToken ct)
    {
        var resource = await RequireResourceAsync(id, ct);
        if (Number(request, "expectedRevision") != resource.Trigger.Revision) throw Conflict();
        var selector = Selector(request.GetProperty("target"));
        var target = await TargetAsync(selector.DefinitionId, selector.SlotName, ct);
        if (target.InputContract is null) throw new TriggerOperationException("unavailable", "The selected serving input contract is unavailable.");
        if (OptionalText(request, "contractRevision") is { } expected && expected != target.ContractRevision) throw Conflict();
        var mappings = ReadMappings(request.GetProperty("mappings"));
        var result = await mapper.MapAsync(target.InputContract, mappings, request.GetProperty("sample").Clone(), ct);
        var fields = target.InputContract.Inputs.Select(input => new { inputName = input.Name,
            state = result.NormalizedInputs.ContainsKey(input.Name) ? (mappings.Any(mapping => mapping.InputName == input.Name) ? "Mapped" : "Defaulted") : "Omitted",
            value = result.NormalizedInputs.TryGetValue(input.Name, out var normalized) ? (JsonElement?)normalized : null });
        return new { advisory = true, contractRevision = target.ContractRevision, fields,
            findings = result.Findings.Select(error => new TriggerFinding(error.ReasonCode, error.Message, InputName: error.InputName)).ToArray() };
    }

    private async ValueTask<TriggerView> ViewAsync(TriggerAggregate aggregate, CancellationToken ct)
    {
        var trigger = aggregate.Trigger; var eligible = 0;
        foreach (var subscription in aggregate.Subscriptions)
            if (subscription.Enabled && await IsEligibleAsync(subscription, ct)) eligible++;
        var available = committer.CanActivateType(trigger.TypeKey, trigger.SchemaVersion);
        var findings = new List<TriggerFinding>();
        if (!available) findings.Add(new("RuntimeSourceUnavailable", "The installed authoring metadata does not provide this type's protected runtime source."));
        var configuration = trigger.Configuration;
        TriggerHttpView? http = null;
        var projectionReady = true;
        if (configuration.TryGetProperty("http", out var httpConfiguration))
        {
            (http, projectionReady) = await HttpReadinessAsync(aggregate, httpConfiguration, findings, ct);
            configuration = Without(configuration, "http");
        }
        var schedule = await ScheduleReadinessAsync(trigger, findings, ct);
        if ((types.Find(trigger.TypeKey)?.Family is "Timer" or "Cron") && trigger.DesiredState == TriggerDesiredState.Enabled && schedule?.IsActive != true)
            projectionReady = false;
        var state = trigger.DesiredState == TriggerDesiredState.Enabled ? (available && projectionReady && eligible > 0 ? "Enabled" : "EnabledUnavailable") : trigger.DesiredState.ToString();
        var manageable = await access.HasAsync(TriggerPermissions.Manage, trigger, ct);
        var operable = await access.HasAsync(TriggerPermissions.Operate, trigger, ct);
        var stoppable = committer.CanStopType(trigger.TypeKey, trigger.SchemaVersion);
        var needsPhysical = trigger.HasActivated || trigger.DesiredState == TriggerDesiredState.Enabled;
        return new(trigger.Id, trigger.Name, trigger.Description, trigger.TypeKey, trigger.SchemaVersion.ToString(CultureInfo.InvariantCulture), trigger.Revision,
            configuration, trigger.Origin, trigger.DesiredState.ToString(), state, aggregate.Subscriptions.Count, eligible,
            findings,
            [new("SaveConfiguration", manageable && !trigger.Origin.ConfigurationReadOnly && (!needsPhysical || available)),
             new("AddSubscription", manageable && (!needsPhysical || available)), new("EnableTrigger", operable && available && eligible > 0 && http?.ReservationState is not ("Unavailable" or "Conflict")),
             new("PauseTrigger", operable && (!needsPhysical || stoppable)), new("DeleteTrigger", manageable && (!needsPhysical || stoppable)),
             new("ReadDeliveries", deliveries?.SupportsInspection == true && await access.HasAsync(TriggerPermissions.DeliveriesRead, trigger, ct))],
            trigger.CreatedAt, trigger.UpdatedAt, http, schedule);
    }

    private async ValueTask<TriggerSubscriptionView> SubscriptionViewAsync(StandaloneTrigger trigger, TriggerSubscription subscription, CancellationToken ct)
    {
        var live = trigger.HasActivated || trigger.DesiredState == TriggerDesiredState.Enabled || subscription.Enabled;
        var writable = !live || committer.CanActivateType(trigger.TypeKey, trigger.SchemaVersion);
        var stoppable = !live || committer.CanStopType(trigger.TypeKey, trigger.SchemaVersion);
        var canRead = await CanReadTargetAsync(subscription.DefinitionId, subscription.SlotName, ct);
        if (!canRead)
        {
            var manageHidden = await access.HasAsync(TriggerPermissions.Manage, trigger, ct);
            var operateHidden = await access.HasAsync(TriggerPermissions.Operate, trigger, ct);
            var authorizeHidden = await access.HasAsync(TriggerPermissions.Authorize, trigger, ct);
            return new(subscription.Id, subscription.TriggerId, subscription.Revision, subscription.Enabled, subscription.Origin,
                new("", ""), [], subscription.Grant is null ? new("Absent") : new(subscription.Grant.Status.ToString(), subscription.Grant.Revision),
                "ValidationPending", [new("TargetDetailsRestricted", "Workflow details require ordinary workflow read permission.")], null,
                [new("SaveSubscription", false, "TargetDetailsRestricted"), new("RemoveSubscription", manageHidden && stoppable && !subscription.Origin.ConfigurationReadOnly),
                 new("EnableSubscription", operateHidden && committer.CanActivateType(trigger.TypeKey, trigger.SchemaVersion) && await IsEligibleAsync(subscription, ct)), new("PauseSubscription", operateHidden && stoppable),
                 new("GrantInvocation", false, "TargetDetailsRestricted"), new("RevokeInvocation", authorizeHidden && stoppable && !subscription.Origin.ConfigurationReadOnly)],
                new { trigger.Name, effectiveState = trigger.DesiredState.ToString(), http = HttpSummary(trigger) });
        }
        var target = await TargetAsync(subscription.DefinitionId, subscription.SlotName, ct);
        var validation = target.InputContract is null ? null : mapper.ValidateMappings(target.InputContract, subscription.Mappings);
        var compatibility = target.State != "Available" || target.InputContract is null ? "ValidationPending" : validation?.IsValid == true ? "Valid" : "Blocked";
        var findings = target.Findings.Concat(validation?.Findings.Select(error => new TriggerFinding(error.ReasonCode, error.Message, InputName: error.InputName)) ?? []).ToArray();
        var manage = await access.HasAsync(TriggerPermissions.Manage, trigger, ct);
        var operate = await access.HasAsync(TriggerPermissions.Operate, trigger, ct);
        var grant = subscription.Grant;
        return new(subscription.Id, subscription.TriggerId, subscription.Revision, subscription.Enabled, subscription.Origin,
            new(subscription.DefinitionId, subscription.SlotName), subscription.Mappings.Select(MappingJson).ToArray(),
            grant is null ? new("Absent") : new(grant.Status.ToString(), grant.Revision, grant.GrantedAt, grant.GrantedBy),
            compatibility, findings, target.Serving,
            [new("SaveSubscription", manage && writable && !subscription.Origin.ConfigurationReadOnly), new("RemoveSubscription", manage && stoppable && !subscription.Origin.ConfigurationReadOnly),
             new("EnableSubscription", operate && compatibility == "Valid" && grant?.Status == TriggerGrantStatus.Granted && committer.CanActivateType(trigger.TypeKey, trigger.SchemaVersion)),
             new("PauseSubscription", operate && stoppable), new("GrantInvocation", writable && !subscription.Origin.ConfigurationReadOnly &&
                 await access.HasAsync(TriggerPermissions.Authorize, trigger, ct) &&
                 await access.HasAsync("workflows/runtime:execute", new TriggerTargetResource(subscription.DefinitionId, subscription.SlotName), ct)),
             new("RevokeInvocation", stoppable && !subscription.Origin.ConfigurationReadOnly && await access.HasAsync(TriggerPermissions.Authorize, trigger, ct))],
            new { trigger.Name, effectiveState = trigger.DesiredState.ToString(), http = HttpSummary(trigger) });
    }

    private async ValueTask ValidateConfigurationAsync(string typeKey, int schema, JsonElement configuration, CancellationToken ct)
    {
        var validating = new StandaloneTriggerConfigurationValidating(typeKey, schema, configuration);
        await events.Publish(validating, ct);
        if (validating.Findings.Count != 0) throw new TriggerOperationException("validation", "Correct the trigger configuration.",
            validating.Findings.GroupBy(finding => finding.Field ?? "configuration", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => string.Join(" ", group.Select(finding => finding.Message)), StringComparer.Ordinal));
    }

    private async ValueTask<bool> IsEligibleAsync(TriggerSubscription subscription, CancellationToken ct)
    {
        if (subscription.Grant?.Status != TriggerGrantStatus.Granted) return false;
        var description = await targets.DescribeAsync(access.Scope, subscription.DefinitionId, subscription.SlotName, ct);
        return description is { State: TriggerTargetState.Available, InputContract: { } contract } &&
            mapper.ValidateMappings(contract, subscription.Mappings).IsValid;
    }

    private ValueTask<bool> CanReadTargetAsync(string id, string slot, CancellationToken ct) =>
        access.HasAsync("workflows/runtime:read", new TriggerTargetResource(id, slot), ct);
    private async ValueTask RequireTargetReadAsync(string id, string slot, CancellationToken ct)
    { if (!await CanReadTargetAsync(id, slot, ct)) throw Missing(); }
    private static TriggerOperationException Missing() => new("missing", "Trigger resource not found.");
    private static TriggerOperationException Conflict() => new("conflict", "The resource or target changed; reload and review the change.");
    private static TriggerOperationException Validation(string message) => new("validation", message);
    private static void CheckLimit(int limit) { if (limit is < 1 or > 100) throw Validation("Page size must be between 1 and 100."); }
    internal static string Text(JsonElement value, string name) => OptionalText(value, name) is { Length: > 0 } text ? text : throw Validation($"'{name}' is required.");
    internal static string? OptionalText(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    internal static long Number(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.TryGetInt64(out var number) ? number : throw Validation($"'{name}' must be an integer.");
    private static bool Flag(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;
    internal static string RequestId(JsonElement request)
    { var id = Text(request, "requestId"); if (id.Length > 128 || id.Any(char.IsControl)) throw Validation("The request identity is invalid."); return id; }
    private static int SchemaVersion(JsonElement request) => int.TryParse(OptionalText(request, "schemaVersion"), NumberStyles.None,
        CultureInfo.InvariantCulture, out var version) && version > 0 ? version : throw Validation("The trigger schema version must be positive.");
    private static TriggerTargetSelector Selector(JsonElement value) => new(Text(value, "definitionId"), OptionalText(value, "slotName") ?? "default");
    private static JsonElement Configuration(JsonElement request)
    {
        var configuration = request.GetProperty("configuration");
        if (configuration.ValueKind != JsonValueKind.Object || configuration.TryGetProperty("http", out _)) throw Validation("Configuration must be an object without reserved HTTP metadata.");
        if (!request.TryGetProperty("http", out var http)) return configuration.Clone();
        return Combine(configuration, "http", http);
    }
    private static JsonElement Combine(JsonElement value, string name, JsonElement property) => JsonSerializer.SerializeToElement(
        value.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal)
            .Append(new KeyValuePair<string, JsonElement>(name, property.Clone())).ToDictionary(item => item.Key, item => item.Value), StandaloneTriggersApi.Json);
    private static JsonElement Without(JsonElement value, string name) => JsonSerializer.SerializeToElement(
        value.EnumerateObject().Where(item => item.Name != name).ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal), StandaloneTriggersApi.Json);
    private static string? EncodeCursor(string? value) => value is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string? DecodeCursor(string? value)
    { try { return value is null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(value)); } catch (FormatException) { throw Validation("The continuation cursor is invalid."); } }
    private static void EnsureReplay(TriggerMutationReceipt receipt, string fingerprint)
    { if (receipt.RequestFingerprint != fingerprint) throw new TriggerOperationException("conflict", "This request identity names different content."); }
    internal static string Fingerprint<T>(T value)
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) Canonical(JsonSerializer.SerializeToElement(value, StandaloneTriggersApi.Json), writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    private static void Canonical(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        { writer.WriteStartObject(); foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Canonical(property.Value, writer); } writer.WriteEndObject(); }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(item, writer); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
}
