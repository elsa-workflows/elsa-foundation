using System.Diagnostics;
using System.Text.Json;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Acme.TriggerAuthoring;

/// <summary>Controlled failures at real public ingress/actor boundaries in a private runner-owned host.</summary>
[ShellFeature("AcceptanceTriggerFaultBoundary", DisplayName = "Acceptance ingress and actor fault fixture")]
public sealed class TriggerFaultFeature : IShellFeature, IPostConfigureShellServices, IMiddlewareShellFeature
{
    public string GateDirectory { get; set; } = "trigger-fault";
    public string OwnerToken { get; set; } = "";
    public string NodeIdentity { get; set; } = "fault-A";
    public string SourceRevision { get; set; } = "";
    public string TriggerId { get; set; } = "";
    public string FaultMode { get; set; } = "";
    public string PersistenceScope { get; set; } = "default";
    public string? TenantId { get; set; } = "default";
    public string? SubscriptionId { get; set; }
    public string? RequestPath { get; set; }
    public int Order => -10;

    public void ConfigureServices(IServiceCollection services)
    {
        if (string.IsNullOrWhiteSpace(GateDirectory) || Path.IsPathRooted(GateDirectory) || string.IsNullOrWhiteSpace(OwnerToken) ||
            string.IsNullOrWhiteSpace(TriggerId) || string.IsNullOrWhiteSpace(NodeIdentity) || NodeIdentity.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')) ||
            SourceRevision.Length is not (40 or 64) || SourceRevision.Any(character => !char.IsAsciiHexDigit(character)) || FaultMode is not ("SourceWriteRefused" or "DispatchShed" or "ResponseWriteFailed") ||
            FaultMode == "ResponseWriteFailed" && (string.IsNullOrWhiteSpace(RequestPath) || !RequestPath.StartsWith("/workflows/http/", StringComparison.Ordinal) || RequestPath.Contains('?')))
            throw new InvalidOperationException("The fault fixture requires its owned directory, token, exact trigger, node, source revision and supported fault mode.");
        services.AddSingleton(new TriggerFaultOptions(GateDirectory, OwnerToken, NodeIdentity, SourceRevision, TriggerId, FaultMode, SubscriptionId, RequestPath, PersistenceScope, TenantId));
        services.AddSingleton<TriggerFaultControl>();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (FaultMode != "DispatchShed") return;
        var actors = services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionActorProvider));
        services.Remove(actors);
        services.Add(new ServiceDescriptor(typeof(IWorkflowExecutionActorProvider), provider => new FaultActors(
            Resolve<IWorkflowExecutionActorProvider>(provider, actors), provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TriggerFaultControl>()), actors.Lifetime));
    }

    private static T Resolve<T>(IServiceProvider provider, ServiceDescriptor descriptor) where T : class =>
        (T)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));

    public void UseMiddleware(IApplicationBuilder app, IHostEnvironment? environment) => app.Use(async (context, next) =>
    {
        var control = context.RequestServices.GetRequiredService<TriggerFaultControl>();
        if (!control.ResponsePathMatches(context.Request.Path.Value)) { await next(); return; }
        var original = context.Response.Body;
        context.Response.Body = new FaultResponseStream(original, context, control);
        try { await next(); }
        finally { context.Response.Body = original; }
    });
}

internal sealed record TriggerFaultOptions(string GateDirectory, string OwnerToken, string NodeIdentity, string SourceRevision, string TriggerId, string FaultMode, string? SubscriptionId, string? RequestPath, string PersistenceScope, string? TenantId);

internal sealed class TriggerFaultControl(TriggerFaultOptions options, IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string bootId = Guid.NewGuid().ToString("N");
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private int attempts;
    internal bool ResponsePathMatches(string? path) => options.FaultMode == "ResponseWriteFailed" && options.RequestPath == path;

    internal async ValueTask BeforeResponseWriteAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Response.StatusCode != 202 || !Applies("ResponseWriteFailed", options.TriggerId)) return;
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("The owned response fixture requires an exact source request key.");
        var scope = context.RequestServices.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope();
        var id = RuntimeStandaloneIngressSource.BuildOutboxItemId(scope, options.TenantId, options.TriggerId, "Http", key);
        var source = await context.RequestServices.GetRequiredService<IRuntimeIngressDeliveryStore>().FindDeliveryAsync(new(scope, options.TenantId), id, cancellationToken);
        if (scope.Value != options.PersistenceScope || source?.StandaloneIngress is not { } actual || actual.TenantId != options.TenantId || actual.TriggerId != options.TriggerId || actual.SourceKey != key ||
            !actual.CanonicalPayload.GetProperty("trigger").GetProperty("configuration").TryGetProperty("sourceAccess", out var access) || access.GetString() != "Anonymous" ||
            source.SubscriptionDelivery?.ControlFacts?.Transport.Any(item => item.State == RuntimeIngressTransportState.AwaitingResponse) != true)
            throw new InvalidDataException("The response-write fixture requires an actual anonymous source accepted and awaiting its original response.");
        await RecordAsync(source, null, true, cancellationToken);
        throw new IOException("Controlled owned-fixture failure on the original HTTP body write after actual AwaitingResponse persistence.");
    }
    internal bool Applies(string mode, string? triggerId, string? subscriptionId = null)
    {
        if (mode != options.FaultMode || triggerId != options.TriggerId) return false;
        if (mode == "DispatchShed" && options.SubscriptionId is not null && subscriptionId != options.SubscriptionId) return false;
        var directory = DirectoryPath();
        var release = Path.Combine(directory, "release-" + options.NodeIdentity + ".json");
        if (File.Exists(release))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(release));
            if (Read(document.RootElement, "ownerToken") == options.OwnerToken && Read(document.RootElement, "bootId") == bootId &&
                Read(document.RootElement, "triggerId") == options.TriggerId) return false;
        }
        return lifetime.Elapsed < TimeSpan.FromSeconds(180);
    }

    internal async ValueTask RecordAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient? recipient,
        bool physicalSourcePresent, CancellationToken cancellationToken)
    {
        var directory = DirectoryPath();
        var marker = new
        {
            processId = Environment.ProcessId, options.NodeIdentity, bootId, options.SourceRevision, options.TriggerId, options.FaultMode,
            attempt = Interlocked.Increment(ref attempts), recordedAt = DateTimeOffset.UtcNow, controlledPublicPortFault = true,
            physicalSourcePresent, deliveryId = source.OutboxItemId, source.SourceFingerprint,
            recipientId = recipient?.RecipientId, subscriptionId = recipient?.SubscriptionId, plannedExecutionId = recipient?.StartIdentity.WorkflowExecutionId,
            capturedTarget = recipient?.Material, sealFingerprint = recipient is null ? null : source.SubscriptionDelivery!.Progress.Single(item => item.RecipientId == recipient.RecipientId).Seal?.Fingerprint,
            claimExpiresAt = source.DeliveryVisibleAfter,
            cohortRecipients = source.SubscriptionDelivery!.Recipients.Select(item => new { recipientId = item.RecipientId,
                plannedExecutionId = item.StartIdentity.WorkflowExecutionId, capturedTarget = item.Material }).ToArray()
        };
        var path = Path.Combine(directory, "fault-" + options.NodeIdentity + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(marker, Json), cancellationToken); File.Move(temporary, path, true); }
        finally { File.Delete(temporary); }
    }

    private string DirectoryPath()
    {
        var root = Path.GetFullPath(environment.ContentRootPath);
        var owner = Path.Combine(root, ".ux-acceptance-owner.json");
        if (!File.Exists(owner) || new FileInfo(owner).LinkTarget is not null) throw new InvalidOperationException("The fault fixture requires its private owned-host marker.");
        using (var document = JsonDocument.Parse(File.ReadAllText(owner)))
            if (Read(document.RootElement, "ownerToken") != options.OwnerToken || !document.RootElement.TryGetProperty("faultFixture", out var enabled) || enabled.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("The fault fixture does not belong to this owned host.");
        var path = Path.GetFullPath(Path.Combine(root, options.GateDirectory));
        var relative = Path.GetRelativePath(root, path);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("The fault gate must remain below its private root.");
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is not null) throw new InvalidOperationException("The fault gate cannot traverse a symbolic directory.");
        }
        Directory.CreateDirectory(path);
        return path;
    }
    private static string? Read(JsonElement root, string field) => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal sealed class FaultResponseStream(Stream selected, HttpContext context, TriggerFaultControl control) : Stream
{
    public override bool CanRead => selected.CanRead;
    public override bool CanSeek => selected.CanSeek;
    public override bool CanWrite => selected.CanWrite;
    public override long Length => selected.Length;
    public override long Position { get => selected.Position; set => selected.Position = value; }
    public override void Flush() => selected.Flush();
    public override Task FlushAsync(CancellationToken ct) => selected.FlushAsync(ct);
    public override int Read(byte[] buffer, int offset, int count) => selected.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => selected.Seek(offset, origin);
    public override void SetLength(long value) => selected.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count)
    {
        control.BeforeResponseWriteAsync(context, context.RequestAborted).AsTask().GetAwaiter().GetResult();
        selected.Write(buffer, offset, count);
    }
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        await control.BeforeResponseWriteAsync(context, ct);
        await selected.WriteAsync(buffer, offset, count, ct);
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        await control.BeforeResponseWriteAsync(context, ct);
        await selected.WriteAsync(buffer, ct);
    }
}

internal sealed class FaultActors(IWorkflowExecutionActorProvider selected, IServiceScopeFactory scopes, TriggerFaultControl control) : IWorkflowExecutionActorProvider, IDisposable, IAsyncDisposable
{
    public WorkflowExecutionActorCapabilities Capabilities => selected.Capabilities;
    public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken ct = default) =>
        new FaultActor(await selected.GetAgentAsync(request, ct), scopes, control);
    public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken ct = default) => selected.PassivateAsync(request, ct);
    public void Dispose() { if (selected is IDisposable disposable) disposable.Dispose(); else if (selected is IAsyncDisposable asyncDisposable) asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    public async ValueTask DisposeAsync() { if (selected is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync(); else if (selected is IDisposable disposable) disposable.Dispose(); }
    private sealed class FaultActor(IWorkflowExecutionActor selected, IServiceScopeFactory scopes, TriggerFaultControl control) : IWorkflowExecutionActor
    {
        public WorkflowExecutionActorDescriptor Descriptor => selected.Descriptor;
        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken ct = default) => EnqueueAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, ct);
        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions options, CancellationToken ct = default)
        {
            if (envelope.Command.Kind == WorkflowExecutionCommandKind.Start && envelope.Command.Payload is { } json &&
                json.Deserialize<WorkflowExecutionStartCommandPayload>()?.StartAuthority?.StandaloneTrigger is { } origin)
            {
                await using var scope = await scopes.CreateAsyncScopeAsync(origin.Scope);
                var actual = await scope.ServiceProvider.GetRequiredService<IRuntimeIngressDeliveryStore>().ReadCommittedRecipientAsync(origin, ct);
                if (control.Applies("DispatchShed", actual.Item.StandaloneIngress?.TriggerId, actual.Recipient.SubscriptionId))
                {
                    if (!JsonElement.DeepEquals(actual.Seal.Command, JsonSerializer.SerializeToElement(envelope)))
                        throw new InvalidDataException("The controlled actor refusal requires the actual exact physical seal.");
                    await control.RecordAsync(actual.Item, actual.Recipient, true, ct);
                    return new(envelope.EnvelopeId, envelope.WorkflowExecutionId, WorkflowExecutionCommandDispatchStatus.Deferred, DateTimeOffset.UtcNow,
                        "Controlled owned-fixture actor capacity refusal after physical source acceptance.",
                        new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchShed] = "true", [RuntimeMetadataKeys.DispatchRetryAfterSeconds] = "1" });
                }
            }
            return await selected.EnqueueAsync(envelope, options, ct);
        }
    }
}
