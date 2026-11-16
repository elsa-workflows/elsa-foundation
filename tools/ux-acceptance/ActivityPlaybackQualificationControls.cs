using CShells.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Workflows.Runtime.Services.History;
using System.Data.Common;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Core;
using OpenIddict.Server;
using OpenIddict.Validation;

// Compiled, registered and mapped only under ELSA_DASHBOARD_QUALIFICATION. Source review precedes
// qualification execution; no production port is replaced and no ordinary host contains these controls.
internal static class ActivityPlaybackQualificationControls
{
    internal static void Register(IServiceCollection services)
    {
        // Instances, rather than per-container factories: CShells clones root registrations. The source
        // clock and the exact-read gate must be the SAME objects in the root and selected shell.
        var state = new PlaybackQualificationState();
        services.AddSingleton(state);
        services.AddSingleton<TimeProvider>(state.Clock);
        // The witness advances general DI time. Keep ordinary credential/session lifetime clocks
        // on wall time through their public options; no lifetime, skew or validation rule changes.
        services.PostConfigure<OpenIddictCoreOptions>(options => options.TimeProvider = TimeProvider.System);
        services.PostConfigure<OpenIddictServerOptions>(options => options.TimeProvider = TimeProvider.System);
        services.PostConfigure<OpenIddictValidationOptions>(options => options.TimeProvider = TimeProvider.System);
        services.PostConfigureAll<CookieAuthenticationOptions>(options => options.TimeProvider = TimeProvider.System);
        services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = TimeProvider.System);
        services.AddSingleton(state.ReadGate);
        services.AddHttpContextAccessor();
        services.AddIncidentStrategy<PlaybackHeldIncidentStrategy>();
        services.ConfigureDbContext<RuntimeSqliteDbContext>((provider, options) =>
            options.AddInterceptors(provider.GetRequiredService<PlaybackReadGate>()));
    }

    internal static void MapEndpoints(IEndpointRouteBuilder endpoints, IShellRegistry registry)
    {
        Protect(endpoints.MapGet("/__qualification/activity-playback/{workflowExecutionId}/admission",
            (string workflowExecutionId, HttpContext http, CancellationToken token) =>
                WithRun(registry, workflowExecutionId, http, token, async (services, run) =>
                {
                    var reader = services.GetService<IRuntimeHistoryStore>();
                    var head = reader is null ? null : await reader.FindHeadAsync(run.WorkflowExecutionId, token);
                    return Results.Json(new { run.WorkflowExecutionId, captureEnabled = run.HistoryCaptureEnabledAtAdmission,
                        policyId = run.HistoryCapturePolicy?.PolicyId, headPresent = head is not null, historyReaderPresent = reader is not null },
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never });
                })));
        Protect(endpoints.MapPost("/__qualification/activity-playback/{workflowExecutionId}/advance-wait",
            (string workflowExecutionId, HttpContext http, PlaybackQualificationState state, CancellationToken token) =>
                WithRun(registry, workflowExecutionId, http, token, (services, run) => AdvanceWait(services, run, state, token))));
        Protect(endpoints.MapPost("/__qualification/activity-playback/{workflowExecutionId}/purge-first-prefix",
            (string workflowExecutionId, HttpContext http, CancellationToken token) =>
                WithRun(registry, workflowExecutionId, http, token, (services, run) => PurgePrefix(services, run, token))));
        Protect(endpoints.MapPost("/__qualification/activity-playback/{workflowExecutionId}/read-hold/{nonce}",
            (string workflowExecutionId, string nonce, HttpContext http, PlaybackQualificationState state, CancellationToken token) =>
                WithRun(registry, workflowExecutionId, http, token, (_, _) => Task.FromResult<IResult>(
                    Guid.TryParseExact(nonce, "N", out _) && state.ReadGate.Arm(workflowExecutionId, nonce)
                        ? Results.Ok(new { workflowExecutionId, nonce }) : Results.Conflict()))));
        Protect(endpoints.MapPost("/__qualification/activity-playback/{workflowExecutionId}/pin-read-fault/{nonce}",
            (string workflowExecutionId, string nonce, HttpContext http, PlaybackQualificationState state, CancellationToken token) =>
                WithRun(registry, workflowExecutionId, http, token, (services, run) => Task.FromResult<IResult>(
                    Guid.TryParseExact(nonce, "N", out _) && state.ReadGate.Arm(workflowExecutionId, nonce, run.PinnedExecutable.ArtifactId)
                        ? Results.Ok(new { workflowExecutionId, nonce, artifactId = run.PinnedExecutable.ArtifactId }) : Results.Conflict()))));
        Protect(endpoints.MapGet("/__qualification/activity-playback/{workflowExecutionId}/read-hold/{nonce}",
            (string workflowExecutionId, string nonce, HttpContext http, PlaybackQualificationState state, CancellationToken token) =>
                WithRun(registry, workflowExecutionId, http, token, (_, _) => Task.FromResult(
                    state.ReadGate.Observe(workflowExecutionId, nonce)))));
        Protect(endpoints.MapDelete("/__qualification/activity-playback/{workflowExecutionId}/read-hold/{nonce}",
            (string workflowExecutionId, string nonce, HttpContext http, PlaybackQualificationState state, CancellationToken token) =>
                WithRun(registry, workflowExecutionId, http, token, (_, _) => Task.FromResult(
                    state.ReadGate.Release(workflowExecutionId, nonce)))));
    }

    private static void Protect(RouteHandlerBuilder route) => route.WithHostOwner("Elsa.Workbench")
        .WithAuthoringModel(EndpointAuthoringModels.MinimalApi).RequirePermission(ModuleManagementPermissionKeys.Manage);

    private static async Task<IResult> WithRun(IShellRegistry registry, string id, HttpContext http,
        CancellationToken token, Func<IServiceProvider, WorkflowExecutionState, Task<IResult>> action)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return Results.BadRequest();
        var shell = await registry.GetOrActivateAsync("default", token);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.Scoped(new PersistenceScope("default")));
        var run = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(id, token);
        if (run is null || run.TenantId is not (null or "default")) return Results.NotFound();
        return await action(services, run);
    }

    private static async Task<IResult> AdvanceWait(IServiceProvider services, WorkflowExecutionState run,
        PlaybackQualificationState state, CancellationToken token)
    {
        if (!await state.ClockChange.WaitAsync(0, token)) return Results.Conflict(new { reason = "clock-operation-active" });
        try
        {
            if (!ReferenceEquals(services.GetRequiredService<TimeProvider>(), state.Clock))
                return Results.Conflict(new { reason = "runtime-clock-not-selected" });
            if (!await SecurityClocksUseWallTime(services))
                return Results.Conflict(new { reason = "security-clock-not-isolated" });
            if (state.AdvancedRuns.Contains(run.WorkflowExecutionId) || state.ReadGate.IsActive)
                return Results.Conflict(new { reason = "witness-already-used-or-read-active" });
            if (run.Status is not (WorkflowExecutionStatus.Running or WorkflowExecutionStatus.Suspended))
                return Results.Conflict(new { reason = "run-not-waiting" });
            var bookmarks = await services.GetRequiredService<IBookmarkStateStore>()
                .ListPageAsync(new BookmarkStatePageQuery(run.WorkflowExecutionId, 2), token);
            if (bookmarks.NextContinuationToken is not null || bookmarks.Items.Count != 1 ||
                bookmarks.Items.Single().StimulusType != EventStimulus.StimulusType)
                return Results.Conflict(new { reason = "one-real-event-wait-required" });
            var bookmark = bookmarks.Items.Single();
            var historyStore = services.GetRequiredService<IRuntimeHistoryStore>();
            var history = await historyStore.ReadPageAsync(
                new RuntimeHistoryQuery(run.WorkflowExecutionId, 2, bookmarkId: bookmark.BookmarkId, name: "BookmarkCreated"), token);
            var recorded = await historyStore.FindBookmarkAsync(run.WorkflowExecutionId, bookmark.BookmarkId, token);
            // Creation and its committed observation use separate real clock calls. Validate each fact
            // at its actual owner; equating those two instants would reject an ordinary progressing clock.
            if (history.Items.Count != 1 || history.Items[0].BookmarkId != bookmark.BookmarkId ||
                history.Items[0].ActivityExecutionId != bookmark.ActivityExecutionId ||
                recorded?.Bookmark.Identity.WorkflowExecutionId != run.WorkflowExecutionId ||
                recorded.Bookmark.Identity.ActivityExecutionId != bookmark.ActivityExecutionId ||
                recorded.Bookmark.CreatedAt != bookmark.CreatedAt)
                return Results.Conflict(new { reason = "native-wait-record-required" });
            var database = services.GetRequiredService<RuntimeSqliteDbContext>();
            if (database.Database.ProviderName != RuntimeSqliteDbContext.ExpectedProviderName)
                return Results.Conflict(new { reason = "owned-sqlite-required" });
            // Refuse a busy owned host, including a live lease which a clock jump could expire.
            // The browser runner is sequential; repeat the actual finite probes after one real settling interval.
            if (!await Quiescent(database, run.WorkflowExecutionId, state.Clock.GetUtcNow(), token))
                return Results.Conflict(new { reason = "owned-host-not-quiescent" });
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, token);
            var stillWaiting = await services.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(run.WorkflowExecutionId, bookmark.BookmarkId, token);
            if (stillWaiting is null || stillWaiting.ActivityExecutionId != bookmark.ActivityExecutionId ||
                stillWaiting.CreatedAt != bookmark.CreatedAt || stillWaiting.StimulusType != bookmark.StimulusType ||
                stillWaiting.StimulusHash != bookmark.StimulusHash ||
                !await Quiescent(database, run.WorkflowExecutionId, state.Clock.GetUtcNow(), token))
                return Results.Conflict(new { reason = "wait-changed-before-clock-advance" });
            token.ThrowIfCancellationRequested();
            var before = state.Clock.GetUtcNow();
            state.Clock.Advance52Minutes();
            state.AdvancedRuns.Add(run.WorkflowExecutionId);
            return Results.Ok(new { run.WorkflowExecutionId, bookmark.BookmarkId, bookmark.ActivityExecutionId,
                bookmark.CreatedAt, creationCommittedAt = history.Items[0].OccurredAt,
                before, after = state.Clock.GetUtcNow(), advancedMinutes = 52,
                sourceClockSelected = true, securityClocksUseWallTime = true,
                schedulerEmpty = true, outboxSettled = true, noLiveLeases = true });
        }
        finally { state.ClockChange.Release(); }
    }

    private static async Task<bool> SecurityClocksUseWallTime(IServiceProvider services)
    {
        if (!ReferenceEquals(services.GetRequiredService<IOptionsMonitor<OpenIddictCoreOptions>>().CurrentValue.TimeProvider, TimeProvider.System) ||
            !ReferenceEquals(services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue.TimeProvider, TimeProvider.System) ||
            !ReferenceEquals(services.GetRequiredService<IOptionsMonitor<OpenIddictValidationOptions>>().CurrentValue.TimeProvider, TimeProvider.System) ||
            !ReferenceEquals(services.GetRequiredService<IOptionsMonitor<SecurityStampValidatorOptions>>().CurrentValue.TimeProvider, TimeProvider.System)) return false;
        var cookies = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var schemes = await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        return schemes.Where(scheme => scheme.HandlerType is not null && typeof(CookieAuthenticationHandler).IsAssignableFrom(scheme.HandlerType))
            .All(scheme => ReferenceEquals(cookies.Get(scheme.Name).TimeProvider, TimeProvider.System));
    }

    private static async Task<bool> Quiescent(RuntimeSqliteDbContext database, string id, DateTimeOffset now, CancellationToken token)
    {
        var encodedId = EfRelationalIdentity.Encode(id);
        var encodedScope = EfRelationalIdentity.Encode("default");
        return !await database.WorkflowExecutionStates.AsNoTracking().AnyAsync(row =>
            (row.ScopeKey != encodedScope || row.WorkflowExecutionId != encodedId) &&
            row.Status != (int)WorkflowExecutionStatus.Completed && row.Status != (int)WorkflowExecutionStatus.Faulted &&
            row.Status != (int)WorkflowExecutionStatus.Cancelled, token) &&
        !await database.SchedulerWorkItems.AsNoTracking().AnyAsync(token) &&
        !await database.RuntimePostCommitOutbox.AsNoTracking().AnyAsync(row => row.Status != (int)RuntimePostCommitOutboxStatus.Delivered &&
            row.Status != (int)RuntimePostCommitOutboxStatus.Cancelled, token) &&
        !await database.ExecutionLivenessStates.AsNoTracking().AnyAsync(row => row.LeaseOwnerId != null && row.LeaseExpiresAtUtcTicks > now.UtcTicks, token);
    }

    private static async Task<IResult> PurgePrefix(IServiceProvider services, WorkflowExecutionState run, CancellationToken token)
    {
        if (!run.Status.IsTerminal()) return Results.Conflict(new { reason = "run-not-terminal" });
        var history = services.GetRequiredService<IRuntimeHistoryStore>();
        var page = await history.ReadPageAsync(new RuntimeHistoryQuery(run.WorkflowExecutionId, 1, name: "ActivityCompleted"), token);
        if (page.Items.Count != 1) return Results.Conflict(new { reason = "retained-completion-required" });
        var original = page.Items[0];
        var through = await services.GetRequiredService<IRuntimeHistoryRetentionStore>()
            .PurgeThroughAsync(run.WorkflowExecutionId, original.Sequence, token);
        var after = await history.FindHeadAsync(run.WorkflowExecutionId, token);
        return Results.Ok(new { run.WorkflowExecutionId, original.HistoryEventId,
            originalSequence = original.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            purgedThrough = through.ToString(System.Globalization.CultureInfo.InvariantCulture),
            afterPurgedThrough = after?.PurgedThroughSequence.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }
}

/// <summary>Only an explicitly authored qualification workflow selects this named strategy.</summary>
[IncidentStrategy("Qualification.PlaybackHeld", "1", DisplayName = "Playback held retry qualification")]
internal sealed class PlaybackHeldIncidentStrategy : IIncidentStrategy
{
    public PlaybackHeldIncidentStrategy() { }
    public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(IncidentResolutionActions.WaitForIntervention());
    }
}

// A fixed private profile: native EF execution/design/publishing stay intact, capture is absent,
// and optional history ports are genuinely unselected after the normal feature composition.
[ShellFeature(name: "PlaybackQualificationWithoutHistory")]
public sealed class PlaybackQualificationWithoutHistoryFeature : IShellFeature, IPostConfigureShellServices
{
    public void ConfigureServices(IServiceCollection services) { }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RuntimeHistoryOptions)))
            throw new InvalidOperationException("The unsupported qualification profile must omit history capture.");
        services.RemoveAll<IRuntimeHistoryStore>();
        // These EF factories require the selected reader. They cannot survive its omission.
        services.RemoveAll<IRuntimeHistoryObservationWriter>();
        services.RemoveAll<IRuntimeHistoryRetentionStore>();
    }
}

internal sealed class PlaybackQualificationState
{
    internal PlaybackQualificationClock Clock { get; } = new();
    internal PlaybackReadGate ReadGate { get; } = new();
    internal SemaphoreSlim ClockChange { get; } = new(1, 1);
    internal HashSet<string> AdvancedRuns { get; } = new(StringComparer.Ordinal);
}

internal sealed class PlaybackQualificationClock : TimeProvider
{
    private long offset;
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow().AddTicks(Interlocked.Read(ref offset));
    public override long GetTimestamp() => System.GetTimestamp();
    public override long TimestampFrequency => System.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => System.LocalTimeZone;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        System.CreateTimer(callback, state, dueTime, period);
    internal void Advance52Minutes() => Interlocked.Add(ref offset, TimeSpan.FromMinutes(52).Ticks);
}

internal sealed class PlaybackReadGate : DbCommandInterceptor
{
    private sealed class Operation(string run, string nonce, string? artifactId)
    {
        internal string Run { get; } = run;
        internal string Nonce { get; } = nonce;
        internal string? ArtifactId { get; } = artifactId;
        internal DateTimeOffset Expires { get; } = TimeProvider.System.GetUtcNow().AddSeconds(30);
        internal bool Entered, Released, TimedOut;
        internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object sync = new();
    private readonly IHttpContextAccessor accessor = new HttpContextAccessor();
    private Operation? operation;
    internal bool IsActive { get { lock (sync) return operation is { Released: false } op && op.Expires > TimeProvider.System.GetUtcNow(); } }
    internal bool Arm(string run, string nonce, string? artifactId = null)
    {
        lock (sync)
        {
            if (IsActive) return false;
            operation?.Continue.TrySetResult(); operation = new Operation(run, nonce, artifactId); return true;
        }
    }
    internal IResult Observe(string run, string nonce)
    {
        lock (sync) return operation is { } op && op.Run == run && op.Nonce == nonce
            ? Results.Ok(new { entered = op.Entered, released = op.Released, timedOut = op.TimedOut }) : Results.NotFound();
    }
    internal IResult Release(string run, string nonce)
    {
        lock (sync)
        {
            if (operation is not { } op || op.Run != run || op.Nonce != nonce) return Results.NotFound();
            op.Released = true; op.Continue.TrySetResult(); return Results.NoContent();
        }
    }
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
        DbDataReader result, CancellationToken token = default)
    {
        var failPin = false;
        lock (sync)
        {
            if (operation is { Released: false, ArtifactId: not null } pin && pin.Expires > TimeProvider.System.GetUtcNow())
            {
                var request = accessor.HttpContext?.Request;
                var artifactId = pin.ArtifactId;
                failPin = request?.Method == "GET" && request.Headers["X-Activity-Playback-Qualification"] == pin.Nonce &&
                    request.Path.Value == $"/runtime/workflows/executables/{artifactId}" &&
                    command.CommandText.Contains($"\"{RuntimeArtifactEfModule.WorkflowExecutableTableName}\"", StringComparison.OrdinalIgnoreCase) &&
                    command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is string value &&
                        (value == EfRelationalIdentity.Encode(artifactId) || value == EfRelationalIdentity.Hash(artifactId)));
                if (failPin) pin.Entered = true;
            }
        }
        if (failPin)
        {
            // The real provider has already executed this exact material read. Dispose its reader
            // before injecting the finite I/O failure; no response body, pin or stored fact is changed.
            await result.DisposeAsync();
            throw new IOException("Owned qualification executable-material read unavailable.");
        }
        Operation selected;
        lock (sync)
        {
            if (operation is not { Entered: false, Released: false, ArtifactId: null } op || op.Expires <= TimeProvider.System.GetUtcNow()) return result;
            var request = accessor.HttpContext?.Request;
            if (request is null || request.Method != "GET" || request.Headers["X-Activity-Playback-Qualification"] != op.Nonce ||
                request.Path.Value != $"/runtime/workflows/instances/{op.Run}/history/occurrences" ||
                !command.CommandText.Contains("elsa_runtime_history_occurrence_member", StringComparison.OrdinalIgnoreCase)) return result;
            op.Entered = true; selected = op;
        }
        var remaining = selected.Expires - TimeProvider.System.GetUtcNow();
        try { await selected.Continue.Task.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, token); }
        catch (TimeoutException) { lock (sync) selected.TimedOut = true; }
        finally { lock (sync) { selected.Released = true; selected.Continue.TrySetResult(); } }
        return result;
    }
}
