using System.Diagnostics;
using System.Text.Json;
using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.TriggerFaults;

/// <summary>Touches no owned persistence descriptor; only refuses the exact added source before its insert.</summary>
[ShellFeature("AcceptanceTriggerSourceWriteFault")]
public sealed class SourceWriteFaultFeature : IShellFeature
{
    public string GateDirectory { get; set; } = "trigger-fault";
    public string OwnerToken { get; set; } = "";
    public string NodeIdentity { get; set; } = "fault-A";
    public string SourceRevision { get; set; } = "";
    public string TriggerId { get; set; } = "";
    public string PersistenceScope { get; set; } = "default";
    public string? TenantId { get; set; } = "default";
    public void ConfigureServices(IServiceCollection services)
    {
        if (string.IsNullOrWhiteSpace(TriggerId) || string.IsNullOrWhiteSpace(OwnerToken) ||
            SourceRevision.Length is not (40 or 64) || SourceRevision.Any(c => !char.IsAsciiHexDigit(c)) ||
            NodeIdentity.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) || string.IsNullOrWhiteSpace(NodeIdentity))
            throw new InvalidOperationException("An exact owned source fault identity is required.");
        services.AddSingleton(new SourceFaultSettings(GateDirectory, OwnerToken, NodeIdentity, SourceRevision, TriggerId, PersistenceScope, TenantId));
        services.AddSingleton<SourceFaultObservation>();
        services.ConfigureDbContext<RuntimeSqliteDbContext>((provider, builder) =>
            builder.AddInterceptors(new SourceFaultInterceptor(provider.GetRequiredService<SourceFaultObservation>())));
    }
}
internal sealed record SourceFaultSettings(string GateDirectory, string OwnerToken, string NodeIdentity, string SourceRevision,
    string TriggerId, string PersistenceScope, string? TenantId);
internal sealed class SourceFaultInterceptor(SourceFaultObservation observation) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (data.Context is not RuntimeSqliteDbContext context) return result;
        foreach (var entry in context.ChangeTracker.Entries<RuntimePostCommitOutboxEntity>().Where(e => e.State == EntityState.Added))
            await observation.BeforeInsertAsync(entry.Entity, cancellationToken);
        return result;
    }
}
internal sealed class SourceFaultObservation(SourceFaultSettings options, IHostEnvironment environment)
{
    private readonly string bootId = Guid.NewGuid().ToString("N");
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private string? refusedKey;
    private int attempts;
    public async ValueTask BeforeInsertAsync(RuntimePostCommitOutboxEntity row, CancellationToken ct)
    {
        if (row.TriggerIdHash != EfRelationalIdentity.Hash(options.TriggerId) ||
            EfRelationalIdentity.Decode(row.ScopeKey) != options.PersistenceScope || lifetime.Elapsed >= TimeSpan.FromSeconds(180)) return;
        using var document = JsonDocument.Parse(row.ContentJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("standaloneIngress", out var source) || source.ValueKind != JsonValueKind.Object) return;
        static string Read(JsonElement value, string key) => EfRelationalIdentity.Decode(value.GetProperty(key).GetString()!);
        var trigger = Read(source, "triggerId"); var key = Read(source, "sourceKey"); var family = Read(source, "sourceFamily");
        var scope = new PersistenceScope(Read(source.GetProperty("scope"), "value"));
        var tenant = source.GetProperty("tenantId").ValueKind == JsonValueKind.Null ? null : Read(source, "tenantId");
        var id = Read(root, "outboxItemId");
        if (trigger != options.TriggerId || scope.Value != options.PersistenceScope || tenant != options.TenantId ||
            id != RuntimeStandaloneIngressSource.BuildOutboxItemId(scope, tenant, trigger, family, key) ||
            id != EfRelationalIdentity.Decode(row.OutboxItemId) || root.GetProperty("subscriptionDelivery").GetProperty("recipients").GetArrayLength() == 0) return;
        Interlocked.CompareExchange(ref refusedKey, key, null);
        if (refusedKey != key) return;
        var directory = DirectoryPath(); var release = Path.Combine(directory, "release-" + options.NodeIdentity + ".json");
        if (File.Exists(release))
        {
            using var released = JsonDocument.Parse(await File.ReadAllTextAsync(release, ct));
            var value = released.RootElement;
            if (value.GetProperty("ownerToken").GetString() == options.OwnerToken && value.GetProperty("bootId").GetString() == bootId &&
                value.GetProperty("triggerId").GetString() == trigger) return;
        }
        var marker = new { processId = Environment.ProcessId, options.NodeIdentity, bootId, options.SourceRevision, triggerId = trigger,
            faultMode = "SourceWriteRefused", attempt = Interlocked.Increment(ref attempts), recordedAt = DateTimeOffset.UtcNow,
            controlledPublicPortFault = true, physicalSourcePresent = false, deliveryId = id,
            sourceFingerprint = Read(root.GetProperty("subscriptionDelivery"), "sourceFingerprint"), sourceKey = key, scope = scope.Value, tenantId = tenant };
        var path = Path.Combine(directory, "fault-" + options.NodeIdentity + ".json"); var temporary = path + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(marker, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ct); File.Move(temporary, path, true); }
        finally { File.Delete(temporary); }
        throw new IOException("Owned fault before the actual new source insert; normal transaction rollback remains responsible.");
    }
    private string DirectoryPath()
    {
        var root = Path.GetFullPath(environment.ContentRootPath); var marker = Path.Combine(root, ".ux-acceptance-owner.json");
        if (!File.Exists(marker) || new FileInfo(marker).LinkTarget is not null) throw new InvalidOperationException("Missing regular owned-host marker.");
        using var document = JsonDocument.Parse(File.ReadAllText(marker));
        if (document.RootElement.GetProperty("ownerToken").GetString() != options.OwnerToken ||
            !document.RootElement.GetProperty("faultFixture").GetBoolean()) throw new InvalidOperationException("Foreign owned-host fault marker.");
        var path = Path.GetFullPath(Path.Combine(root, options.GateDirectory)); var relative = Path.GetRelativePath(root, path);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Fault directory must remain below its private root.");
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        { current = Path.Combine(current, part); if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is not null) throw new InvalidOperationException("Symbolic fault directory."); }
        Directory.CreateDirectory(path); return path;
    }
}
