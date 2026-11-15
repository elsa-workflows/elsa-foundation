using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

// This one-shot setup uses normal service contracts against a stopped runner-owned host. It exposes no listener.
var request = JsonSerializer.Deserialize<SetupRequest>(await Console.In.ReadToEndAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
              ?? throw new InvalidOperationException("An owned setup request is required.");
var root = Path.GetFullPath(request.ContentRoot);
if (request.Action is not ("seed" or "cleanup")) throw new InvalidOperationException("Unsupported acceptance setup action.");
var sourceOwnedId = request.ResourceId ?? $"ux-source-owned-{Guid.NewGuid():N}";
if (!sourceOwnedId.StartsWith("ux-source-owned-", StringComparison.Ordinal) || sourceOwnedId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
    throw new InvalidOperationException("The source-owned acceptance identity is invalid.");
if (!Path.IsPathFullyQualified(request.ContentRoot) || new DirectoryInfo(root).LinkTarget is not null)
    throw new InvalidOperationException("Setup requires the owned absolute content directory.");
var marker = JsonSerializer.Deserialize<OwnerMarker>(await File.ReadAllTextAsync(Path.Combine(root, ".ux-acceptance-owner.json")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web));
if (string.IsNullOrWhiteSpace(request.OwnerToken) || marker?.OwnerToken != request.OwnerToken || marker.BackendStopped != true)
    throw new InvalidOperationException("The runner must stop its owned backend before setup.");
var database = Path.Combine(root, "elsa.db");
if (!File.Exists(database) || new FileInfo(database).LinkTarget is not null)
    throw new InvalidOperationException("Setup requires the owned host's existing regular database.");
var services = new ServiceCollection();
services.AddLogging();
services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new IdentityIamEntityFrameworkCoreOptions
    { Provider = "Sqlite", ConnectionString = $"Data Source={database}" }, isDevelopmentOrDemo: true);
services.AddWorkflowRuntime();
services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
{
    Provider = "Sqlite", ConnectionString = $"Data Source={database}",
    RecoveryContinuationSigningKey = "development-demo-only-recovery-continuation-key",
    HierarchyCursorSigningKey = "development-demo-only-hierarchy-cursor-signing-key"
});
await using var provider = services.BuildServiceProvider();
var actors = new List<object>();
var actorSpecifications = new[]
{
    ("acceptance-reader", "default", new[] { "triggers:read", "triggers/deliveries:read" }),
    ("acceptance-denied", "default", Array.Empty<string>()),
    ("acceptance-other-tenant", "ux-other", new[] { "*" })
};
foreach (var (name, tenant, permissions) in actorSpecifications.Where(_ => request.Action == "seed"))
{
    // Basic Studio bootstrap/preferences are independent of access to trigger resources.
    var grants = permissions.Concat(["api-capabilities:read", "identity/providers:read", "studio/preferences:read", "studio/preferences:write"])
        .ToHashSet(StringComparer.Ordinal);
    await using var scope = provider.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    sp.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
    var users = sp.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
    var user = await users.FindByNameAsync(name);
    if (user is null)
    {
        user = new() { Id = $"ux-{name}", TenantId = tenant, UserName = name, DisplayName = name, Email = $"{name}@example.test" };
        var created = await users.CreateAsync(user, "Password123!");
        if (!created.Succeeded) throw new InvalidOperationException("Acceptance identity setup failed: " + string.Join(",", created.Errors.Select(x => x.Code)));
    }
    var roleId = $"ux-role-{name}";
    var roles = sp.GetRequiredService<IRevisionAwareRoleStore>();
    var role = await roles.FindWithRevisionAsync(tenant, roleId);
    var savedRole = await roles.SaveWithRevisionAsync(new(roleId, tenant, name, "Owned acceptance role", grants, false), role?.Revision);
    if (savedRole.Status != IamRevisionSaveStatus.Saved) throw new InvalidOperationException("Acceptance role revision was refused.");
    var records = sp.GetRequiredService<IRevisionAwareUserStore>();
    var record = await records.FindWithRevisionAsync(tenant, user.Id) ?? throw new InvalidOperationException("The real user manager did not persist its user.");
    var savedUser = await records.SaveWithRevisionAsync(record.Record with { RoleIds = new HashSet<string> { roleId } }, record.Revision);
    if (savedUser.Status != IamRevisionSaveStatus.Saved) throw new InvalidOperationException("Acceptance user revision was refused.");
    var memberships = sp.GetRequiredService<IRevisionAwareTenantMembershipStore>();
    var membership = await memberships.FindWithRevisionAsync(tenant, user.Id);
    var savedMembership = await memberships.SaveWithRevisionAsync(new(tenant, user.Id, TenantMembershipStatus.Active,
        new HashSet<string> { roleId }, new HashSet<string>()), membership?.Revision);
    if (savedMembership.Status != IamRevisionSaveStatus.Saved) throw new InvalidOperationException("Acceptance tenant membership revision was refused.");
    actors.Add(new { userName = name, userId = user.Id, tenantId = tenant, permissions = grants });
}
await using (var scope = provider.CreateAsyncScope())
{
    var sp = scope.ServiceProvider;
    var persistence = new PersistenceScope("default");
    sp.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(persistence));
    var resourceScope = new TriggerScope(persistence, "default");
    var existing = await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(resourceScope, sourceOwnedId);
    if (request.Action == "cleanup" && existing is not null && existing.Trigger.DesiredState != TriggerDesiredState.Deleted)
    {
        var previous = existing.Trigger;
        var deleted = new StandaloneTrigger(previous.Id, previous.Name, previous.TypeKey, previous.SchemaVersion,
            previous.Configuration, previous.Revision + 1, previous.ConfigurationRevision, previous.AssociationRevision,
            TriggerDesiredState.Deleted, previous.Origin, previous.CreatedAt, DateTimeOffset.UtcNow, previous.CreatedBy,
            previous.Description, previous.HasActivated, previous.TenantId);
        await sp.GetRequiredService<ITriggerManagementCommitter>().CommitAsync(resourceScope,
            new($"{sourceOwnedId}-delete-{deleted.Revision}", $"{sourceOwnedId}-delete-{deleted.Revision}", "acceptance-source-provider", previous.Revision, deleted, existing.Subscriptions));
    }
    else if (request.Action == "seed" && (existing is null || existing.Trigger.DesiredState == TriggerDesiredState.Deleted))
    {
        var now = DateTimeOffset.UtcNow;
        var revision = (existing?.Trigger.Revision ?? 0) + 1;
        var trigger = new StandaloneTrigger(sourceOwnedId, "Source-owned acceptance Event", "event", 1,
            JsonSerializer.SerializeToElement(new { eventName = "ux.source-owned" }), revision, revision, 0, TriggerDesiredState.Paused,
            new(TriggerManagementKind.Provider, "acceptance-source-provider", true), now, now, "acceptance-source-provider", tenantId: "default");
        await sp.GetRequiredService<ITriggerManagementCommitter>().CommitAsync(resourceScope,
            new($"{sourceOwnedId}-create-{revision}", $"{sourceOwnedId}-create-{revision}", "acceptance-source-provider", existing?.Trigger.Revision, trigger, []));
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { actors, sourceOwnedTriggerId = sourceOwnedId, physicalDatabase = "elsa.db" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

internal sealed record SetupRequest(string ContentRoot, string OwnerToken, string Action, string? ResourceId);
internal sealed record OwnerMarker(string OwnerToken, bool BackendStopped);
