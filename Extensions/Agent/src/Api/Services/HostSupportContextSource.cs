using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Elsa.Agent.Api.Constants;
using Elsa.Agent.Api.Endpoints;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Api;

/// <summary>Admits a browser observation as a claim and reads this backend under the real owning session.</summary>
internal sealed class HostSupportContextSource(IServiceProvider services, IHttpContextAccessor accessor,
    IAgentSessionService sessions) : IAgentContextProvider
{
    private const string SourceId = "agent.host-support";
    private const string ModuleId = "Elsa.Agent";
    private const string Kind = "agent.host-support";
    private const int MaxContentLength = 32768;
    private const int MaxRows = 128;
    private static readonly AgentContextProviderDescriptor Contract = new(SourceId, ModuleId, Kind,
        Array.AsReadOnly(new[] { Kind }), Array.AsReadOnly(new[] { "1" }), Array.Empty<string>());

    public AgentContextProviderDescriptor Descriptor => Contract;

    public async ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.CallerAttachments.Count != 1) return Refused("agent.context.support_invalid");
        var caller = request.CallerAttachments.Single();
        if (caller.ModuleId != ModuleId || caller.ContentType != Kind || caller.SchemaVersion != "1" || !TryStudio(caller.Content, out var studio))
            return Refused("agent.context.support_invalid");
        var session = await CurrentSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return Refused("agent.context.support_denied");
        return await CollectCurrentAsync(session, studio, caller.Sensitivity, cancellationToken);
    }

    public async ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Results.Count != 0 || request.Context.Any(item => !ValidRetained(item)))
            return Refused("agent.context.support_invalid");
        var session = await CurrentSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return Refused("agent.context.support_denied");
        // Success admits the original sanitized historical claims unchanged. The current projection
        // omits the old browser claim rather than presenting it as today's installed Studio state.
        return await CollectCurrentAsync(session, null, AgentContextSensitivity.Internal, cancellationToken);
    }

    private async ValueTask<AgentContextContribution> CollectCurrentAsync(AgentSession session, JsonElement? studio,
        AgentContextSensitivity sensitivity, CancellationToken token)
    {
        var policy = session.Policy with
        {
            AllowedContextKinds = Array.AsReadOnly(session.Policy.AllowedContextKinds.ToArray()),
            DeniedCapabilityIds = Array.AsReadOnly(session.Policy.DeniedCapabilityIds.ToArray())
        };
        AgentSupportView support;
        try
        {
            // Resolve at the scoped read boundary: the default source itself enumerates context
            // descriptors, so eagerly injecting it into this provider would make a constructor cycle.
            support = await services.GetRequiredService<IAgentSupportSource>().ReadAsync(session.ActorId, session.TenantId, policy, token);
            token.ThrowIfCancellationRequested();
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
            support = new(AgentSupportCompleteness.Unavailable, [], []);
        }
        var current = await CurrentSessionAsync(session.Id, token);
        if (current is null) return Refused("agent.context.support_denied");
        if (current.PolicyRevision != session.PolicyRevision || !SamePolicy(current.Policy, policy))
            return Refused("agent.context.support_changed");
        if (!TryBackend(new("backend", "session", session.PolicyRevision, support), out var backend))
        {
            // A replacement read contract cannot turn arbitrary strings or an oversized result into
            // provider content. Unknown support remains an explicit observation, never empty complete.
            backend = JsonSerializer.SerializeToElement(new BackendSupportView("backend", "session", session.PolicyRevision,
                new(AgentSupportCompleteness.Unavailable, [], [])), SupportContextJson.Default.BackendSupportView);
        }
        var attachments = new List<AgentContextAttachment>();
        if (studio is { } browser)
            attachments.Add(Attachment("agent.studio-support", "studio", "Studio support observation",
                "Client-asserted Studio discovery and registration observations; these grant no resource or execution authority.",
                browser, AgentContextOrigin.ClientSnapshot,
                sensitivity > AgentContextSensitivity.Internal ? sensitivity : AgentContextSensitivity.Internal));
        attachments.Add(Attachment("agent.backend-support", "backend", "Backend support observation",
            "Installed backend support and current session admission hints; concrete resource permission and execution remain unchecked.",
            backend, AgentContextOrigin.AuthoritativeRead, AgentContextSensitivity.Internal));
        token.ThrowIfCancellationRequested();
        return new(attachments, []);
    }

    private async Task<AgentSession?> CurrentSessionAsync(string sessionId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var context = accessor.HttpContext;
        var principal = context?.User;
        bool CurrentRequest() => ReferenceEquals(accessor.HttpContext, context) && ReferenceEquals(context?.User, principal);
        var actor = principal is null ? null : AgentEndpointActor.Resolve(principal);
        var tenant = principal is null ? null : AgentEndpointActor.ResolveTenant(principal);
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(tenant)) return null;
        var authorities = services.GetServices<IPermissionAuthorizationService>().Take(2).ToArray();
        if (authorities.Length != 1) return null;
        var decision = await authorities[0].AuthorizeAsync(new(principal, AgentPermissionKeys.Use, tenant), token);
        token.ThrowIfCancellationRequested();
        if (!CurrentRequest() || !decision.Succeeded) return null;
        var owner = await sessions.FindOwnerAsync(sessionId, token);
        token.ThrowIfCancellationRequested();
        if (!CurrentRequest() || owner is null || !AgentEndpointActor.CanAccess(owner.ActorId, owner.TenantId, principal)) return null;
        var session = await sessions.FindAsync(sessionId, token);
        token.ThrowIfCancellationRequested();
        // Recheck the actual current principal after awaited owner/hydration reads as well.
        return CurrentRequest() && session is not null && principal.Identity?.IsAuthenticated == true
            && AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, principal)
            && session.ActorId == owner.ActorId && session.TenantId == owner.TenantId ? session : null;
    }

    private static AgentContextAttachment Attachment(string id, string source, string label, string summary, JsonElement content,
        AgentContextOrigin origin, AgentContextSensitivity sensitivity) => new(id, source, null, label, Kind, sensitivity,
        "host", summary, content, new Dictionary<string, string>())
        { ModuleId = ModuleId, SchemaVersion = "1", Origin = origin, AgentScopes = [] };

    private static AgentContextContribution Refused(string code) => new([], [new(SourceId, code,
        "Current host support observation could not be admitted. Refresh the current context before retrying.", true)]);

    private static bool SamePolicy(AgentPolicy left, AgentPolicy right) => left.Id == right.Id && left.Enabled == right.Enabled
        && left.ContextVisibility == right.ContextVisibility && left.MaxContextSensitivity == right.MaxContextSensitivity
        && left.AutonomyMode == right.AutonomyMode && left.MaxAutonomyMode == right.MaxAutonomyMode && left.RetentionLabel == right.RetentionLabel
        && left.AllowedContextKinds.SequenceEqual(right.AllowedContextKinds, StringComparer.Ordinal)
        && left.DeniedCapabilityIds.SequenceEqual(right.DeniedCapabilityIds, StringComparer.Ordinal);

    private static bool ValidRetained(AgentContextAttachment item)
    {
        if (item.ModuleId != ModuleId || item.ContentType != Kind || item.SchemaVersion != "1" || item.Target is not null
            || item.Client is not null || item.AgentScopes.Count != 0 || item.References.Count != 0 || item.SourceId is not null)
            return false;
        if (item.Sensitivity == AgentContextSensitivity.SecretRedacted && item.Content is null)
            return item.Source is "studio" or "backend" && item.Scope == "redacted"
                && item.Label == "Redacted context" && item.Summary == "Secret context was redacted before provider use.";
        if (item.Content is not JsonElement original) return false;
        if (item.Source == "studio" && item.Origin == AgentContextOrigin.ClientSnapshot && TryStudio(original, out var studio))
            return JsonElement.DeepEquals(original, studio);
        if (item.Source != "backend" || item.Origin != AgentContextOrigin.AuthoritativeRead || original.GetRawText().Length > MaxContentLength)
            return false;
        try
        {
            var value = original.Deserialize(SupportContextJson.Default.BackendSupportView);
            return value is not null && TryBackend(value, out var backend) && JsonElement.DeepEquals(original, backend);
        }
        catch (JsonException) { return false; }
    }

    private static bool TryStudio(object? content, out JsonElement projected)
    {
        projected = default;
        if (content is not JsonElement { ValueKind: JsonValueKind.Object } original || original.GetRawText().Length > MaxContentLength
            || HasDuplicateProperties(original)) return false;
        try
        {
            var value = original.Deserialize(SupportContextJson.Default.StudioSupportView);
            if (value is null || value.Side != "studio" || !value.ClientAsserted
                || value.Completeness is not ("complete" or "incomplete" or "unknown" or "unavailable")
                || value.RegistrationCompleteness is not ("complete" or "incomplete")
                || value.Modules is null || value.Loader is null || value.Registrations is null
                || value.Modules.Count > MaxRows || value.Loader.Count > MaxRows || value.Registrations.Count > MaxRows
                || value.Modules.Any(row => row is null || !Identifier(row.Id) || !Label(row.DisplayName)
                    || row.Status is not ("available" or "disabled" or "failed" or "incompatible" or "unknown"))
                || value.Modules.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Modules.Count
                || value.Loader.Any(row => row is null || !Identifier(row.ModuleId)
                    || row.Status is not ("registering" or "loaded" or "failed" or "retired"))
                || value.Loader.Select(row => row.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Loader.Count
                || value.Registrations.Any(row => row is null || !Identifier(row.Kind) || !Identifier(row.Id)
                    || row.OriginModuleId is not null && !Identifier(row.OriginModuleId)
                    || row.DeclaredModuleId is not null && !Identifier(row.DeclaredModuleId)
                    || row.ResourceModuleIds is not null && (row.ResourceModuleIds.Count > 8 || row.ResourceModuleIds.Any(id => !Identifier(id)))
                    || row.Status is not ("available" or "unavailable" or "unknown"))
                || value.Registrations.Select(row => (row.Kind, row.Id)).Distinct().Count() != value.Registrations.Count) return false;
            var modules = value.Modules.ToList();
            var loader = value.Loader.ToList();
            var registrations = value.Registrations.ToList();
            value = value with { Modules = modules, Loader = loader, Registrations = registrations };
            // JSON.stringify and the backend's default encoder have different lengths for valid
            // localized/HTML labels. Bound the actual accepted encoding without rejecting the prompt
            // or falsely claiming that rows omitted during normalization were absent.
            while (true)
            {
                projected = JsonSerializer.SerializeToElement(value, SupportContextJson.Default.StudioSupportView);
                if (projected.GetRawText().Length <= 24576) return true;
                if (registrations.Count > 0)
                {
                    registrations.RemoveAt(registrations.Count - 1);
                    value = value with { RegistrationCompleteness = "incomplete" };
                }
                else if (loader.Count > 0)
                {
                    loader.RemoveAt(loader.Count - 1);
                    value = value with { RegistrationCompleteness = "incomplete" };
                }
                else if (modules.Count > 0)
                {
                    modules.RemoveAt(modules.Count - 1);
                    value = value with { Completeness = "incomplete" };
                }
                else return false;
            }
        }
        catch (JsonException) { return false; }
    }

    private static bool TryBackend(BackendSupportView value, out JsonElement projected)
    {
        projected = default;
        var support = value.Support;
        if (value.Side != "backend" || value.PolicyScope != "session" || support is null
            || !Enum.IsDefined(support.Completeness) || support.Tools is null || support.Contexts is null
            || support.Tools.Count + support.Contexts.Count > 64 || support.Tools.Any(row => row is null)
            || support.Contexts.Any(row => row is null || row.ContextKinds is null || row.SchemaVersions is null || row.ResourceTypes is null)) return false;
        // A replacement can return mutable list implementations; accepted content owns its snapshot.
        support = support with
        {
            Tools = Array.AsReadOnly(support.Tools.ToArray()),
            Contexts = Array.AsReadOnly(support.Contexts.Select(row => row with
            {
                ContextKinds = Array.AsReadOnly(row.ContextKinds.ToArray()),
                SchemaVersions = Array.AsReadOnly(row.SchemaVersions.ToArray()),
                ResourceTypes = Array.AsReadOnly(row.ResourceTypes.ToArray())
            }).ToArray())
        };
        if (support.Tools.Any(row => row is null || !NativeId(row.Name) || !NativeId(row.ModuleId) || !NativeId(row.ContractVersion)
                || !Enum.IsDefined(row.ExecutionLocation) || !Enum.IsDefined(row.Admission) || !Enum.IsDefined(row.Authority))
            || support.Tools.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != support.Tools.Count
            || support.Contexts.Any(row => row is null || !NativeId(row.Id) || !NativeId(row.ModuleId) || !NativeId(row.ScopeKind)
                || !Values(row.ContextKinds) || !Values(row.SchemaVersions) || !Values(row.ResourceTypes)
                || !Enum.IsDefined(row.Admission) || row.Authority != AgentSupportStatus.Unchecked)
            || support.Contexts.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != support.Contexts.Count) return false;
        projected = JsonSerializer.SerializeToElement(value with { Support = support }, SupportContextJson.Default.BackendSupportView);
        return projected.GetRawText().Length <= MaxContentLength;
    }

    private static bool Values(IReadOnlyList<string>? values) => values is { Count: <= 8 } && values.All(NativeId);
    private static bool NativeId(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':' or '*');
    private static bool Identifier(string? value) => value is { Length: > 0 and <= 160 }
        && Regex.IsMatch(value, @"^@?[A-Za-z0-9_-]+(?:[./:][A-Za-z0-9_-]+)*$");
    private static bool Label(string? value) => value is { Length: > 0 and <= 256 } && !string.IsNullOrWhiteSpace(value)
        && !value.Any(character => character <= 0x1f || character == 0x7f);
    private static bool HasDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (HasDuplicateProperties(item)) return true;
        return false;
    }
}

internal sealed record StudioSupportView(string? Side, bool ClientAsserted, string? Completeness,
    IReadOnlyList<StudioSupportModule>? Modules, string? RegistrationCompleteness,
    IReadOnlyList<StudioSupportLoader>? Loader, IReadOnlyList<StudioSupportRegistration>? Registrations);
internal sealed record StudioSupportModule(string? Id, string? DisplayName, string? Status);
internal sealed record StudioSupportLoader(string? ModuleId, string? Status);
internal sealed record StudioSupportRegistration(string? Kind, string? Id, string? OriginModuleId, string? DeclaredModuleId,
    IReadOnlyList<string>? ResourceModuleIds, string? Status);
internal sealed record BackendSupportView(string Side, string PolicyScope, long? PolicyRevision, AgentSupportView Support);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(AgentCamelCaseEnumConverter)], GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StudioSupportView))]
[JsonSerializable(typeof(BackendSupportView))]
internal partial class SupportContextJson : JsonSerializerContext;
