using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

// This fixture deliberately uses only the pinned Weaver 1.0 public surface.
// No reflection, optional new members, payload backdating or digest construction.
namespace Acme.Home.Weaver;

internal static class UpgradeContract
{
    internal const string Module = "Acme.Upgrade";
    internal const string Read = "acme_upgrade_read_widget";
    internal const string Write = "acme_upgrade_set_visibility";
    internal const string Admin = "acme_upgrade_admin_visibility";
    internal const string Widget = "acme.home:status";
    internal const string Resource = "home-widget";
    internal const string Context = "acme.upgrade.widget";
    internal const string Source = "acme.upgrade.widget-context";
    internal const string Result = "acme.upgrade.widget-result";
    internal const string Endpoint = "/_qualification/acme/upgrade-widget";
    internal const string ReadPermission = "studio/preferences:read";
    internal const string WritePermission = "studio/preferences:write";
    internal static readonly string[] Scopes = ["/qualification/acme-home", "/", "/dashboard", "/overview"];
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static WeaverResourceTarget Target(UpgradeObservation value) => new(Module, Resource, Widget, value.Document.Revision);
    internal static object State(UpgradeObservation value) => new { widgetId = Widget, revision = value.Document.Revision,
        visible = value.Visible, hostId = value.Key.StudioHostId };
    internal static WeaverPayload Payload(UpgradeObservation value) => new(Result, "1", JsonSerializer.SerializeToElement(State(value), Json));
    internal static WeaverPayload Preview(UpgradeObservation value, bool visible) => new(Result, "1", JsonSerializer.SerializeToElement(new {
        widgetId = Widget, revision = value.Document.Revision, beforeVisible = value.Visible, visible, hostId = value.Key.StudioHostId }, Json));
    internal static bool Own(WeaverResourceTarget? target) => target is not null && target.ModuleId == Module
        && target.ResourceType == Resource && target.ResourceId == Widget && !string.IsNullOrWhiteSpace(target.Revision);
    internal static void Refuse(string code, string message, int status = 409) => throw new UpgradeRefusal(new("acme.upgrade." + code, message, status));
}

internal sealed class UpgradeRefusal(WeaverError error) : Exception(error.Message)
{
    internal WeaverError Error { get; } = error;
}
internal sealed record UpgradeSettings(string HostId, bool OwnerAvailable);
internal sealed record UpgradeObservation(StudioPreferenceKey Key, StudioPreferenceDocument Document, bool Visible);

[ShellFeature("AcmeWeaverUpgradeResource", DisplayName = "Independent legacy Weaver upgrade contribution",
    DependsOn = new object[] { "WeaverApi", "StudioPreferences" })]
public class UpgradeResourceFeature : IWebShellFeature
{
    public string StudioHostId { get; set; } = string.Empty;
    public string ExecutionObservationFile { get; set; } = string.Empty;
    public bool OwnerAvailable { get; set; } = true;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        if (string.IsNullOrWhiteSpace(StudioHostId) || StudioHostId.Length > 128
            || StudioHostId.Any(value => !(char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-')))
            throw new InvalidOperationException("Upgrade contribution requires a fixed valid StudioHostId.");
        WidgetFixtureFiles.Check(ExecutionObservationFile);
        services.AddHttpContextAccessor();
        services.AddSingleton(new UpgradeSettings(StudioHostId, OwnerAvailable));
        services.AddSingleton(new WidgetEffectsSettings(ExecutionObservationFile));
        services.TryAddSingleton<WidgetExecutionJournal>();
        services.TryAddScoped<UpgradeOwner>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, UpgradeContext>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverCapabilityProvider, UpgradeCapabilities>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, UpgradeReadTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, UpgradeWriteTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, UpgradeAdminTool>());
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        // The root-owned T038 inspector uses real store APIs and native owner authorization.
        UpgradeRecordInspection.MapEndpoints(endpoints);
        endpoints.MapGet(UpgradeContract.Endpoint, async (UpgradeOwner owner, HttpContext http, CancellationToken ct) => {
            try {
                var observed = await owner.ObserveAsync(null, null, false, ct);
                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(UpgradeContract.State(observed));
            } catch (UpgradeRefusal refusal) { return Results.Problem(statusCode: refusal.Error.StatusCode, title: refusal.Error.Code, detail: refusal.Error.Message); }
        }).RequireAuthorization();
        endpoints.MapGet(UpgradeContract.Endpoint + "/execution-entries", async (UpgradeOwner owner, WidgetExecutionJournal journal, HttpContext http, CancellationToken ct) => {
            try {
                var key = await owner.AuthorizeAsync(null, null, false, ct);
                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(new { entries = journal.Read(key) });
            } catch (UpgradeRefusal refusal) { return Results.Problem(statusCode: refusal.Error.StatusCode, title: refusal.Error.Code, detail: refusal.Error.Message); }
            catch (WidgetFixtureException failure) { return Results.Problem(statusCode: 503, title: "acme.upgrade.execution_observation_unavailable",
                extensions: new Dictionary<string, object?> { ["requiresFreshObservationPath"] = true, ["invalidationRetained"] = failure.ObservationInvalidationRetained }); }
        }).RequireAuthorization();
    }
}

internal sealed class UpgradeOwner(IHttpContextAccessor http, IAuthSessionService auth,
    IPermissionAuthorizationService permissions, IStudioPreferenceService preferences, UpgradeSettings settings)
{
    internal async Task<StudioPreferenceKey> AuthorizeAsync(string? actor, string? tenant, bool write, CancellationToken ct)
    {
        var context = http.HttpContext;
        var principal = context?.User;
        if (principal?.Identity?.IsAuthenticated != true) UpgradeContract.Refuse("unauthenticated", "A current personal session is required.", 403);
        var session = await auth.GetAsync(principal!, ct);
        if (!ReferenceEquals(context, http.HttpContext) || !ReferenceEquals(principal, context!.User)
            || session.Status != "authenticated" || string.IsNullOrWhiteSpace(session.Subject) || string.IsNullOrWhiteSpace(session.TenantId)
            || session.Subject == StudioHomeLayout.DefaultSubjectId || actor is not null && actor != session.Subject || tenant is not null && tenant != session.TenantId)
            UpgradeContract.Refuse("scope_denied", "The current native actor and tenant must match.", 403);
        var supplied = context.Request.Headers["X-Elsa-Studio-Host-Id"];
        if (supplied.Count > 0 && (supplied.Count != 1 || supplied[0] != settings.HostId))
            UpgradeContract.Refuse("host_denied", "The configured personal owner host is required.", 403);
        var key = new StudioPreferenceKey(session.Subject!, session.TenantId!, settings.HostId, StudioPreferenceNamespaces.HomeLayout);
        foreach (var permission in write ? new[] { UpgradeContract.ReadPermission, UpgradeContract.WritePermission } : new[] { UpgradeContract.ReadPermission }) {
            var decision = await permissions.AuthorizeAsync(new(principal!, permission, key.TenantId, key) { CancellationToken = ct }, ct);
            if (!ReferenceEquals(context, http.HttpContext) || !ReferenceEquals(principal, context.User) || !decision.Succeeded)
                UpgradeContract.Refuse("permission_denied", "Current native permission is required.", 403);
        }
        return key;
    }

    internal async Task<UpgradeObservation> ObserveAsync(string? actor, string? tenant, bool write, CancellationToken ct)
    {
        var key = await AuthorizeAsync(actor, tenant, write, ct);
        if (!settings.OwnerAvailable) UpgradeContract.Refuse("owner_unavailable", "The owning resource is unavailable.", 503);
        var document = await preferences.FindAsync(key, ct);
        if (document is null || document.Namespace != StudioPreferenceNamespaces.HomeLayout || document.SchemaVersion != StudioHomeLayout.SchemaVersion
            || string.IsNullOrWhiteSpace(document.Revision) || !StudioHomeLayout.IsValid(document.Value))
            UpgradeContract.Refuse("layout_invalid", "An existing valid personal Home layout is required.");
        var widgets = document!.Value.GetProperty("widgets").EnumerateArray().Where(row => row.GetProperty("id").GetString() == UpgradeContract.Widget).ToArray();
        if (widgets.Length != 1 || !widgets[0].TryGetProperty("visible", out var visible) || visible.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            UpgradeContract.Refuse("widget_missing", "One existing personal status card with explicit visibility is required.");
        return new(key, document, widgets[0].GetProperty("visible").GetBoolean());
    }

    internal async Task<WeaverToolResult> ApplyAsync(WeaverPreparedToolInvocation invocation, bool visible, CancellationToken ct)
    {
        var before = await ObserveAsync(invocation.ActorId, invocation.TenantId, true, ct);
        if (!UpgradeContract.Own(invocation.Target) || invocation.Target!.Revision != before.Document.Revision)
            UpgradeContract.Refuse("stale", "The reviewed native layout revision changed.");
        if (before.Visible == visible || !JsonElement.DeepEquals(invocation.Preview.Data, UpgradeContract.Preview(before, visible).Data))
            UpgradeContract.Refuse("review_invalid", "The exact prepared visibility change is required.");
        var document = JsonNode.Parse(before.Document.Value.GetRawText())!.AsObject();
        document["widgets"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == UpgradeContract.Widget)!["visible"] = visible;
        var value = JsonSerializer.SerializeToElement(document);
        ct.ThrowIfCancellationRequested();
        StudioPreferenceDocument saved;
        try { saved = await preferences.WriteAsync(before.Key, new(before.Document.SchemaVersion, value),
            StudioPreferenceWriteCondition.Matches(before.Document.Revision), ct); }
        catch (StudioPreferenceConflictException) { UpgradeContract.Refuse("conflict", "The native CAS refused the stale revision."); throw; }
        catch (Exception) { return Uncertain(invocation); }
        try {
            var after = await ObserveAsync(invocation.ActorId, invocation.TenantId, false, CancellationToken.None);
            if (saved.Revision != after.Document.Revision || after.Document.Revision == before.Document.Revision || after.Visible != visible
                || !JsonElement.DeepEquals(after.Document.Value, value)) return Uncertain(invocation);
            var payload = new WeaverPayload(UpgradeContract.Result, "1", JsonSerializer.SerializeToElement(new {
                widgetId = UpgradeContract.Widget, revision = after.Document.Revision, beforeVisible = before.Visible,
                visible, hostId = after.Key.StudioHostId }, UpgradeContract.Json));
            return new(invocation.ToolCallId, invocation.ToolName, true, "Native personal visibility changed.", payload) {
                Receipt = new(invocation.InvocationId, invocation.Target!, UpgradeContract.Target(after), WeaverEffectScope.ServerPersisted,
                    new(UpgradeContract.Result, "1", payload.Data.Clone())) };
        } catch (Exception) { return Uncertain(invocation); }
    }

    private static WeaverToolResult Uncertain(WeaverPreparedToolInvocation invocation) => new(invocation.ToolCallId, invocation.ToolName, false,
        "Native persistence could not be confirmed. Do not replay; reread the owning layout.");
}

internal abstract class UpgradeTool(UpgradeOwner owner, WidgetExecutionJournal journal, string name, WeaverRisk risk) : IWeaverTool, IWeaverToolAuthorization
{
    private const string BooleanSchema = """{"type":"object","required":["visible"],"properties":{"visible":{"type":"boolean"}},"additionalProperties":false}""";
    public WeaverToolDescriptor Descriptor { get; } = new(name, name, "Read or review the existing personal status card through its native owner.",
        risk == WeaverRisk.ReadOnly ? WeaverToolMutability.ReadOnly : WeaverToolMutability.Mutating, risk,
        risk == WeaverRisk.ReadOnly ? """{"type":"object","properties":{},"additionalProperties":false}""" : BooleanSchema,
        risk == WeaverRisk.ReadOnly ? [UpgradeContract.ReadPermission] : [UpgradeContract.ReadPermission, UpgradeContract.WritePermission], UpgradeContract.Scopes) {
        ModuleId = UpgradeContract.Module, ResultType = UpgradeContract.Result, ResultSchemaVersion = "1", DisclosureSourceId = UpgradeContract.Source };

    public async ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
    {
        try {
            _ = await owner.ObserveAsync(actorId, tenantId, risk != WeaverRisk.ReadOnly, cancellationToken);
            if (target is not null && !UpgradeContract.Own(target)) UpgradeContract.Refuse("scope_denied", "Foreign resource target.", 403);
            return new(true, []);
        } catch (UpgradeRefusal refusal) { return new(false, [new(refusal.Error.Code, refusal.Error.Message)]); }
    }

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        try {
            if (invocation.ToolName != name || risk != WeaverRisk.ReadOnly && !invocation.AllowMutations)
                UpgradeContract.Refuse("arguments_invalid", "This request does not admit the selected operation.", 400);
            var state = await owner.ObserveAsync(invocation.ActorId, invocation.TenantId, risk != WeaverRisk.ReadOnly, cancellationToken);
            var arguments = JsonSerializer.SerializeToElement(invocation.Arguments);
            if (risk == WeaverRisk.ReadOnly) {
                if (invocation.Arguments.Count != 0) UpgradeContract.Refuse("arguments_invalid", "Read requires an empty object.", 400);
                return WeaverResult<WeaverToolPreparation>.Success(new(UpgradeContract.Target(state), arguments, risk, UpgradeContract.Payload(state), false));
            }
            var visible = Boolean(arguments);
            if (visible == state.Visible) UpgradeContract.Refuse("noop", "The native card already has this visibility.");
            return WeaverResult<WeaverToolPreparation>.Success(new(UpgradeContract.Target(state), arguments, risk, UpgradeContract.Preview(state, visible), true));
        } catch (UpgradeRefusal refusal) { return new(default, refusal.Error); }
    }

    public async Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        // This exact journal is independently provisioned; missing/failed is never observed zero.
        var actual = await owner.AuthorizeAsync(null, null, false, cancellationToken);
        journal.Record(actual, invocation, name);
        try {
            if (invocation.ToolName != name || invocation.ModuleId != UpgradeContract.Module || invocation.ToolContractVersion != "1"
                || invocation.Risk != risk || invocation.ExecutionLocation != WeaverExecutionLocation.Server || invocation.Client is not null
                || invocation.ExpiresAt <= DateTimeOffset.UtcNow || invocation.RequiresApproval != (risk != WeaverRisk.ReadOnly)
                || string.IsNullOrWhiteSpace(invocation.InvocationId) || !UpgradeContract.Own(invocation.Target))
                UpgradeContract.Refuse("prepared_invalid", "The frozen native preparation is invalid.");
            if (risk == WeaverRisk.ReadOnly) {
                var state = await owner.ObserveAsync(invocation.ActorId, invocation.TenantId, false, cancellationToken);
                if (invocation.Arguments.ValueKind != JsonValueKind.Object || invocation.Arguments.EnumerateObject().Any()
                    || invocation.Target!.Revision != state.Document.Revision || !JsonElement.DeepEquals(invocation.Preview.Data, UpgradeContract.Payload(state).Data))
                    UpgradeContract.Refuse("stale", "The prepared native read changed.");
                return WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, name, true, "Observed native personal visibility.", UpgradeContract.Payload(state)));
            }
            return WeaverResult<WeaverToolResult>.Success(await owner.ApplyAsync(invocation, Boolean(invocation.Arguments), cancellationToken));
        } catch (UpgradeRefusal refusal) { return new(default, refusal.Error); }
    }

    private static bool Boolean(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject().Count() != 1
            || !arguments.TryGetProperty("visible", out var visible) || visible.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            UpgradeContract.Refuse("arguments_invalid", "Exactly one boolean visible argument is required.", 400);
        return arguments.GetProperty("visible").GetBoolean();
    }
}

internal sealed class UpgradeReadTool(UpgradeOwner owner, WidgetExecutionJournal journal) : UpgradeTool(owner, journal, UpgradeContract.Read, WeaverRisk.ReadOnly);
internal sealed class UpgradeWriteTool(UpgradeOwner owner, WidgetExecutionJournal journal) : UpgradeTool(owner, journal, UpgradeContract.Write, WeaverRisk.ReviewRequired);
internal sealed class UpgradeAdminTool(UpgradeOwner owner, WidgetExecutionJournal journal) : UpgradeTool(owner, journal, UpgradeContract.Admin, WeaverRisk.Admin);

internal sealed class UpgradeCapabilities : IWeaverCapabilityProvider
{
    public ValueTask<IReadOnlyCollection<WeaverCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<WeaverCapability>>([
            new("acme.upgrade.read", UpgradeContract.Module, "Read native upgrade card", "Read current personal visibility.", WeaverCapabilityKind.Answer,
                WeaverRisk.ReadOnly, UpgradeContract.Scopes, [UpgradeContract.ReadPermission], [UpgradeContract.Context]) { Modes = ["explain"] },
            new("acme.upgrade.write", UpgradeContract.Module, "Review native upgrade card", "Review a native personal visibility change.", WeaverCapabilityKind.Proposal,
                WeaverRisk.ReviewRequired, UpgradeContract.Scopes, [UpgradeContract.ReadPermission, UpgradeContract.WritePermission], [UpgradeContract.Context]) { Modes = ["build"] },
            new("acme.upgrade.admin", UpgradeContract.Module, "Review legacy Admin operation", "Prepare actual legacy Admin risk without new members.", WeaverCapabilityKind.Proposal,
                WeaverRisk.Admin, UpgradeContract.Scopes, [UpgradeContract.ReadPermission, UpgradeContract.WritePermission], [UpgradeContract.Context]) { Modes = ["build"] }
        ]);
}

internal sealed class UpgradeContext(UpgradeOwner owner, IWeaverSessionService sessions) : IWeaverContextProvider
{
    public WeaverContextProviderDescriptor Descriptor { get; } = new(UpgradeContract.Source, UpgradeContract.Module, "acme-upgrade",
        [UpgradeContract.Context], ["1"], [UpgradeContract.Resource]);

    public async ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
    {
        try {
            var session = await sessions.FindAsync(request.SessionId, cancellationToken);
            if (session is null || session.Status != WeaverSessionStatus.Active) UpgradeContract.Refuse("session_denied", "A current native Weaver session is required.", 403);
            var state = await owner.ObserveAsync(session!.ActorId, session.TenantId, false, cancellationToken);
            if (request.Target is not null && (!UpgradeContract.Own(request.Target) || request.Target.Revision != state.Document.Revision)
                || request.CallerAttachments.Any(item => item.ModuleId != UpgradeContract.Module || item.ContentType != UpgradeContract.Context
                    || !UpgradeContract.Own(item.Target) || item.Target!.Revision != state.Document.Revision))
                UpgradeContract.Refuse("context_stale", "A current native owner reference is required.");
            return new([new("acme.upgrade:" + state.Document.Revision, UpgradeContract.Context, UpgradeContract.Widget, "Native personal upgrade card",
                UpgradeContract.Context, WeaverContextSensitivity.Internal, "/qualification/acme-home", "Current visibility reread from the native Home owner.",
                UpgradeContract.State(state), new Dictionary<string, string>()) { ModuleId = UpgradeContract.Module, SchemaVersion = "1",
                Target = UpgradeContract.Target(state), Origin = WeaverContextOrigin.AuthoritativeRead, WeaverScopes = UpgradeContract.Scopes }], []);
        } catch (UpgradeRefusal refusal) { return new([], [new(Descriptor.Id, refusal.Error.Code, refusal.Error.Message, true)]); }
    }

    public async ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
    {
        try {
            var session = await sessions.FindAsync(request.SessionId, cancellationToken);
            if (session is null || session.Status != WeaverSessionStatus.Active) UpgradeContract.Refuse("session_denied", "Current native ownership is required.", 403);
            var state = await owner.ObserveAsync(session!.ActorId, session.TenantId, false, cancellationToken);
            foreach (var item in request.Context)
                if (item.ModuleId != UpgradeContract.Module || item.SchemaVersion != "1" || item.ContentType != UpgradeContract.Context
                    || !UpgradeContract.Own(item.Target) || !request.IsHistorical && !request.IsReceiptContinuation && item.Target!.Revision != state.Document.Revision)
                    UpgradeContract.Refuse("disclosure_invalid", "The retained owner context is foreign or stale.");
            foreach (var result in request.Results)
                if (!UpgradeContract.Own(result.Access?.Target ?? result.Receipt?.After))
                    UpgradeContract.Refuse("disclosure_invalid", "The retained result has no matching native target.");
            return new(request.IsReceiptContinuation ? [] : request.Context, []);
        } catch (UpgradeRefusal refusal) { return new([], [new(Descriptor.Id, refusal.Error.Code, refusal.Error.Message, true)]); }
    }
}

// Names consumed by the reused journal, kept out of product/provider code.
internal static class WidgetEffects { internal static readonly string[] Tools = [UpgradeContract.Read, UpgradeContract.Write, UpgradeContract.Admin]; }
internal sealed record WidgetEffectsSettings(string ExecutionObservationFile);
internal sealed class WidgetFixtureException(string message, Exception inner, bool? observationInvalidationRetained = null) : Exception(message, inner)
{
    internal bool? ObservationInvalidationRetained { get; } = observationInvalidationRetained;
}
internal static class WidgetFixtureFiles
{
    internal static void Check(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path || Path.GetDirectoryName(path) is not { } parent
            || !Directory.Exists(parent) || new DirectoryInfo(parent).LinkTarget is not null || new FileInfo(path).LinkTarget is not null || Directory.Exists(path))
            throw new InvalidDataException("Upgrade observations require an absolute regular file in an existing private directory.");
        if (OperatingSystem.IsWindows()) throw new InvalidDataException("Upgrade observations require POSIX permissions.");
        const UnixFileMode other = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((File.GetUnixFileMode(parent) & other) != 0 || File.Exists(path) && (File.GetUnixFileMode(path) & other) != 0)
            throw new InvalidDataException("Upgrade observations require private permissions.");
    }
}
