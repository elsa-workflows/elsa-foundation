using System.Diagnostics;
using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.TriggerAuthoring;

/// <summary>Owned acceptance only: observes the real physical seal before delegating to the selected native adapter.</summary>
[ShellFeature("AcceptanceTriggerDispatchBoundary", DisplayName = "Acceptance dispatch boundary fixture")]
public sealed class DispatchBoundaryFeature : IShellFeature, IPostConfigureShellServices
{
    public string GateDirectory { get; set; } = "";
    public string OwnerToken { get; set; } = "";
    public string NodeIdentity { get; set; } = "";
    public string SourceRevision { get; set; } = "";
    public string? TriggerId { get; set; }
    public bool BlockAfterFirstSeal { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        if (string.IsNullOrWhiteSpace(GateDirectory) || Path.IsPathRooted(GateDirectory) || string.IsNullOrWhiteSpace(OwnerToken) ||
            string.IsNullOrWhiteSpace(NodeIdentity) || NodeIdentity.Length > 80 || NodeIdentity.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')) ||
            SourceRevision.Length is not (40 or 64) || SourceRevision.Any(character => !char.IsAsciiHexDigit(character)) || TriggerId is not null && string.IsNullOrWhiteSpace(TriggerId))
            throw new InvalidOperationException("The dispatch fixture requires an owned relative gate directory, owner token, node identity and exact source revision.");
        services.AddSingleton(new DispatchBoundaryOptions(GateDirectory, OwnerToken, NodeIdentity, SourceRevision, TriggerId, BlockAfterFirstSeal));
        services.AddSingleton<DispatchBoundaryGate>();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        var selected = services.Where(descriptor => descriptor.ServiceType == typeof(IStandaloneTriggerRuntimeTarget) && !descriptor.IsKeyedService).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("The dispatch fixture requires an already selected runtime target.");
        foreach (var descriptor in selected)
        {
            services.Remove(descriptor);
            services.Add(new ServiceDescriptor(typeof(IStandaloneTriggerRuntimeTarget), provider => new DispatchBoundaryTarget(
                ResolveSelected(provider, descriptor), provider.GetRequiredService<IRuntimeIngressDeliveryStore>(), provider.GetRequiredService<DispatchBoundaryGate>(),
                disposeSelected: descriptor.ImplementationInstance is null), descriptor.Lifetime));
        }
    }

    private static IStandaloneTriggerRuntimeTarget ResolveSelected(IServiceProvider provider, ServiceDescriptor descriptor) =>
        (IStandaloneTriggerRuntimeTarget)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
}

internal sealed record DispatchBoundaryOptions(string GateDirectory, string OwnerToken, string NodeIdentity,
    string SourceRevision, string? TriggerId, bool BlockAfterFirstSeal);

internal sealed class DispatchBoundaryGate(DispatchBoundaryOptions options, IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MaximumBlock = TimeSpan.FromSeconds(180);
    private readonly string bootId = Guid.NewGuid().ToString("N");
    private string? blockedSeal;

    internal bool Matches(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient) => recipient.ModelKey == "native" &&
        (options.TriggerId is null || source.StandaloneIngress?.TriggerId == options.TriggerId);

    internal async ValueTask ObserveAsync(RuntimeCommittedRecipient committed, string phase, bool blocking,
        RuntimeTargetAcceptanceReceipt? receipt, CancellationToken cancellationToken)
    {
        var directory = RequireOwnedDirectory();
        var item = committed.Item;
        var recipient = committed.Recipient;
        var seal = committed.Seal;
        var marker = new
        {
            processId = Environment.ProcessId, options.NodeIdentity, bootId, sourceRevision = options.SourceRevision,
            deliveryId = item.OutboxItemId, recipientId = recipient.RecipientId, plannedExecutionId = recipient.StartIdentity.WorkflowExecutionId,
            capturedTarget = recipient.Material, item.SourceFingerprint, selectionFingerprint = item.SubscriptionDelivery!.SelectionFingerprint,
            sealFingerprint = seal.Fingerprint, phase, blockAfterFirstSeal = blocking, recordedAt = DateTimeOffset.UtcNow,
            claimExpiresAt = item.DeliveryVisibleAfter, claimOwnerId = item.DeliveringOwnerId, claimFencingToken = item.DeliveryFencingToken,
            cohortRecipients = item.SubscriptionDelivery.Recipients.Select(selected => new
            {
                recipientId = selected.RecipientId, subscriptionId = selected.SubscriptionId, plannedExecutionId = selected.StartIdentity.WorkflowExecutionId,
                capturedTarget = selected.Material, phase = item.SubscriptionDelivery.Progress.Single(progress => progress.RecipientId == selected.RecipientId).Phase.ToString()
            }).ToArray(),
            acceptanceReceipt = receipt is null ? null : new { kind = receipt.Kind.ToString(), receipt.EvidenceId, receipt.SealFingerprint }
        };
        var target = Path.Combine(directory, "dispatch-" + options.NodeIdentity + ".json");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(marker, Json), cancellationToken);
            File.Move(temporary, target, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal bool TryBlock(RuntimeCommittedRecipient committed)
    {
        if (!options.BlockAfterFirstSeal) return false;
        var previous = Interlocked.CompareExchange(ref blockedSeal, committed.Seal.Fingerprint, null);
        // Every retry of the same physical seal remains stopped, including a new claimant on this process.
        return previous is null || previous == committed.Seal.Fingerprint;
    }

    internal async ValueTask WaitForReleaseAsync(RuntimeCommittedRecipient committed, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < MaximumBlock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = RequireOwnedDirectory();
            var release = Path.Combine(directory, "release-" + options.NodeIdentity + ".json");
            if (File.Exists(release))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(release, cancellationToken));
                    var root = document.RootElement;
                    if (String(root, "ownerToken") == options.OwnerToken && String(root, "bootId") == bootId &&
                        String(root, "deliveryId") == committed.Item.OutboxItemId && String(root, "recipientId") == committed.Recipient.RecipientId &&
                        String(root, "sealFingerprint") == committed.Seal.Fingerprint) return;
                }
                catch (JsonException) { /* A partial owner-file write cannot release an accepted command. */ }
                catch (IOException) { /* The runner may atomically replace its release file; read it again. */ }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        throw new TimeoutException("The owned acceptance dispatch fixture reached its bounded wait before release.");
    }

    private string RequireOwnedDirectory()
    {
        var root = Path.GetFullPath(environment.ContentRootPath);
        var marker = Path.Combine(root, ".ux-acceptance-owner.json");
        if (!File.Exists(marker) || new FileInfo(marker).LinkTarget is not null)
            throw new InvalidOperationException("The dispatch fixture requires its private owned-host marker.");
        using (var document = JsonDocument.Parse(File.ReadAllText(marker)))
            if (String(document.RootElement, "ownerToken") != options.OwnerToken ||
                !document.RootElement.TryGetProperty("dispatchFixture", out var enabled) || enabled.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("The dispatch fixture does not belong to this owned host.");
        var directory = Path.GetFullPath(Path.Combine(root, options.GateDirectory));
        var relative = Path.GetRelativePath(root, directory);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("The acceptance gate must remain beneath its private host root.");
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is not null)
                throw new InvalidOperationException("The acceptance gate cannot traverse a symbolic directory.");
        }
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string? String(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString() : null;
}

internal sealed class DispatchBoundaryTarget(IStandaloneTriggerRuntimeTarget selected, IRuntimeIngressDeliveryStore ingress,
    DispatchBoundaryGate gate, bool disposeSelected) : IStandaloneTriggerRuntimeTarget, IDisposable, IAsyncDisposable
{
    public string ModelKey => selected.ModelKey;
    public bool Supports(WorkflowExecutable? nativeWorkflow, RuntimeModelMaterial? model) => selected.Supports(nativeWorkflow, model);
    public ValueTask<RuntimeDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        CancellationToken cancellationToken = default) => selected.PrepareDispatchAsync(source, recipient, cancellationToken);
    public async ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        var receipt = await selected.ReconcileAsync(source, recipient, seal, cancellationToken);
        if (receipt is not null && gate.Matches(source, recipient))
            await gate.ObserveAsync(await ReadCommittedAsync(source, recipient, seal, cancellationToken), "TargetAccepted", false, receipt, cancellationToken);
        return receipt;
    }

    public async ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        if (!gate.Matches(source, recipient))
        {
            await selected.DispatchAsync(source, recipient, seal, cancellationToken);
            return;
        }
        var committed = await ReadCommittedAsync(source, recipient, seal, cancellationToken);
        var progress = committed.Item.SubscriptionDelivery!.Progress.Single(item => item.RecipientId == recipient.RecipientId);
        if (progress.Phase != RuntimeRecipientPhase.DispatchCommitted)
            throw new InvalidDataException("The acceptance boundary did not observe the exact physical committed dispatch seal.");
        var blocking = gate.TryBlock(committed);
        await gate.ObserveAsync(committed, "DispatchCommitted", blocking, null, cancellationToken);
        if (blocking) await gate.WaitForReleaseAsync(committed, cancellationToken);
        await selected.DispatchAsync(committed.Item, committed.Recipient, committed.Seal, cancellationToken);
        var receipt = await selected.ReconcileAsync(committed.Item, committed.Recipient, committed.Seal, cancellationToken);
        await gate.ObserveAsync(committed, receipt is null ? "EnqueueReturned" : "TargetAccepted", false, receipt, cancellationToken);
    }

    private async ValueTask<RuntimeCommittedRecipient> ReadCommittedAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient,
        RuntimeDispatchSeal seal, CancellationToken cancellationToken)
    {
        var committed = await ingress.ReadCommittedRecipientAsync(new RuntimeIngressCommandOrigin(source.OutboxItemId, source.SourceFingerprint,
            source.SubscriptionDelivery!.SelectionFingerprint, recipient.StartIdentity.Scope, recipient.RecipientId), cancellationToken);
        if (!committed.Recipient.IsEquivalentTo(recipient) || !committed.Seal.IsEquivalentTo(seal) ||
            !committed.Seal.StartIdentity.IsEquivalentTo(recipient.StartIdentity))
            throw new InvalidDataException("The acceptance boundary did not observe the exact physical committed dispatch seal.");
        return committed;
    }

    public async ValueTask DisposeAsync()
    {
        if (!disposeSelected) return;
        if (selected is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        else if (selected is IDisposable disposable) disposable.Dispose();
    }

    public void Dispose()
    {
        if (!disposeSelected) return;
        if (selected is IDisposable disposable) disposable.Dispose();
        else if (selected is IAsyncDisposable asyncDisposable) asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
