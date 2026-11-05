// Compiled outside the repository against the owned normal Workbench binary closure.
// This is a bounded operator/fixture client, never an HTTP endpoint or history-row seeder.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    if (args.Length != 2) throw new ArgumentException("Supply the controller-owned root and nonce.");
    var root = Path.GetFullPath(args[0]);
    var marker = JsonSerializer.Deserialize<Owner>(await File.ReadAllTextAsync(Path.Combine(root, "owned-history.json")), json)
        ?? throw new InvalidOperationException("The owned marker is absent.");
    if (marker.SchemaVersion != 1 || marker.Nonce != args[1] || marker.Root != root ||
        !Directory.GetParent(root)!.Name.StartsWith("elsa-history-diagnostics-", StringComparison.Ordinal) ||
        Process.GetProcessById(marker.ControllerProcessId).HasExited || ProcessAlive(marker.BackendPid) ||
        new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))
        throw new InvalidOperationException("The controller does not own a stopped diagnostics backend at this root.");
    var database = Path.Combine(root, "elsa.db");
    if (!File.Exists(database) || Directory.EnumerateFiles(root, "*.db*").Any(file => File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)))
        throw new InvalidOperationException("The normal owned Workbench must create a non-linked database first.");
    var input = Path.Combine(root, "owned-history-request.json");
    if (new FileInfo(input).Length > 16_384) throw new InvalidOperationException("The private fixture request exceeded its bound.");
    var request = JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(input), json)
        ?? throw new InvalidOperationException("The bounded fixture request is absent.");
    if (request.Nonce != marker.Nonce || request.SourceSha != marker.SourceSha ||
        request.Project is not ("diagnostics-desktop-light" or "diagnostics-320-dark") || request.Accounts.Length != 3 ||
        request.Accounts.Select(account => account.UserName).Distinct(StringComparer.Ordinal).Count() != 3 ||
        request.Accounts.Any(account => !account.UserName.StartsWith("history-probe-", StringComparison.Ordinal) || account.TenantId != "default" ||
            string.IsNullOrWhiteSpace(account.Password) || !ValidPermissions(account.Profile, account.Permissions)))
        throw new InvalidOperationException("The requested subjects differ from the controller's bounded ordinary IAM profiles.");

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddWorkflowRuntime();
    services.AddRuntimeHistory();
    services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new IdentityIamEntityFrameworkCoreOptions
    { Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False" }, isDevelopmentOrDemo: true);
    services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
    {
        Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False", Pooling = false, CacheWorkflowExecutables = false,
        RecoveryContinuationSigningKey = "development-demo-only-recovery-continuation-key",
        HierarchyCursorSigningKey = "development-demo-only-hierarchy-cursor-signing-key"
    });
    await using var provider = services.BuildServiceProvider();
    var accounts = new List<object>();
    foreach (var account in request.Accounts)
    {
        await using var scope = provider.CreateAsyncScope();
        var sdk = scope.ServiceProvider;
        sdk.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default")));
        var users = sdk.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var roles = sdk.GetRequiredService<Elsa.Foundation.Identity.Core.Iam.IRoleManager>();
        var user = new AspNetCoreIdentityUser { UserName = account.UserName, TenantId = "default", Email = account.UserName + "@example.test", EmailConfirmed = true, DisplayName = account.UserName };
        Require(await users.CreateAsync(user, account.Password));
        var roleName = account.UserName + "-role";
        var role = await roles.CreateAsync(new("default", roleName, "Owned history diagnostics", account.Permissions.ToHashSet(StringComparer.Ordinal)));
        Require(await users.AddToRoleAsync(user, roleName));
        var stored = await sdk.GetRequiredService<Elsa.Foundation.Identity.Core.Iam.IUserStore>().FindAsync("default", user.Id)
            ?? throw new InvalidOperationException("The ordinary IAM write is unavailable.");
        if (stored.RoleIds.Count != 1 || !stored.RoleIds.Contains(role.Id) || stored.DirectPermissions.Count != 0)
            throw new InvalidOperationException("The diagnostics identity did not retain its exact ordinary role.");
        accounts.Add(new { account.UserName, account.Profile, userId = user.Id, roleId = role.Id, permissions = role.Permissions.Order(StringComparer.Ordinal).ToArray() });
    }

    await using var runtimeScope = provider.CreateAsyncScope();
    var runtime = runtimeScope.ServiceProvider;
    runtime.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new("default")));
    var executions = runtime.GetRequiredService<IWorkflowExecutionStateStore>();
    var history = runtime.GetRequiredService<IRuntimeHistoryStore>();
    var completed = await executions.FindAsync(request.CompletedRun)
        ?? throw new InvalidOperationException("The actual completed run is absent.");
    var head = await history.FindHeadAsync(request.CompletedRun)
        ?? throw new InvalidOperationException("The actual retained history is absent.");
    if (completed.Status != WorkflowExecutionStatus.Completed || completed.TenantId != "default" ||
        request.ThroughSequence < head.FirstSequence || request.ThroughSequence >= head.LastSequence)
        throw new InvalidOperationException("Retention requires an actual completed owned run and a proper prefix.");
    var purgedThrough = await runtime.GetRequiredService<IRuntimeHistoryRetentionStore>().PurgeThroughAsync(request.CompletedRun, request.ThroughSequence);
    if (purgedThrough < request.ThroughSequence) throw new InvalidOperationException("The actual retention port did not retain the requested boundary.");

    // Deliberate lower-level native legacy-continuation fixture. The normal catalog's Event/UserTask
    // inputs have no expiry setting. No activity activation, stock authorable expiry or callback result is claimed.
    // A real normally published artifact supplies the physical pin; the normal fenced commit and CreateBookmark
    // paths create the native owner and handle and capture their own history atomically.
    var executable = await runtime.GetRequiredService<IWorkflowExecutableStore>().FindAsync(request.ExpiryTemplateArtifactId)
        ?? throw new InvalidOperationException("The normally published expiry-template artifact is absent.");
    var node = executable.RootActivity;
    var targets = executable.ResumeTargets.Values.Where(target => target.ExecutableNodeId == node.ExecutableNodeId).ToArray();
    if (node.ActivityType != "Elsa.Event" || targets.Length != 1)
        throw new InvalidOperationException("The native legacy fixture requires the exact published Event root and one real resume target.");
    var run = "history-probe-expired-" + marker.Nonce;
    var activityId = run + ":legacy-wait";
    var bookmarkId = activityId + ":handle";
    if (await executions.FindAsync(run) is not null) throw new InvalidOperationException("The native fixture already exists.");
    var now = DateTimeOffset.UtcNow;
    var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true,
        ["WorkflowStarted", "ActivityStarted", "ActivitySuspended", "BookmarkCreated"]);
    var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory };
    var workflow = new WorkflowExecutionState(run, executable.Identity, WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, "default", metadata)
    { HistoryCaptureEnabledAtAdmission = true, HistoryCapturePolicy = policy };
    var activity = new ActivityExecutionState(new(activityId, run, node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion),
        ActivityExecutionStatus.Running, "NativeLegacyWaitFixture", now, now, null, null, null, null, null, 0, [], [], 0, 0, metadata)
    { ExecutionScopeId = "scope:root" };
    var ownership = runtime.GetRequiredService<IRuntimeExecutionOwnershipService>();
    var ownershipContext = runtime.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>();
    var lease = await ownership.AcquireAsync(run);
    object expiry;
    try
    {
        using var fence = ownershipContext.Push(lease);
        var committer = runtime.GetRequiredService<RuntimeCheckpointCommitter>();
        var checkpoint = new RuntimeCheckpoint(run + ":admission", RuntimeCheckpointNames.WorkflowStarted, run, now, [activityId], metadata);
        var admitted = await committer.CommitAsync(new(run + ":initial-commit", checkpoint,
            new(new(run, RuntimeStateChangeOperation.Upsert, workflow, metadata), null,
                [new(activityId, RuntimeStateChangeOperation.Upsert, activity, metadata)], [], [], [], []), [], metadata));
        if (!admitted.Succeeded) throw new InvalidOperationException("The native initial checkpoint was not acknowledged.");
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(10);
        var payload = new RuntimeCreateBookmarkCommandPayload(executable.Identity, bookmarkId, activityId, node.ExecutableNodeId,
            targets[0].ResumeTargetId, "history.native-legacy-expiry", run + ":stimulus", null, expiresAt,
            RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason);
        var work = new RuntimeSchedulerWorkItem(run + ":create-bookmark", run, run + ":command", WorkflowExecutionCommandKind.CreateBookmark,
            run + ":envelope", run + ":idempotency", now, now, 1, JsonSerializer.SerializeToElement(payload), metadata, metadata);
        var handler = new WorkflowCreateBookmarkSchedulerWorkHandler(runtime.GetRequiredService<IWorkflowExecutableStore>(),
            runtime.GetRequiredService<IActivityExecutionStateStore>(), committer,
            new RuntimeActivityExecutionInspectionAccumulator(runtime.GetRequiredService<IActivityExecutionInspectionStore>()), TimeProvider.System);
        await handler.HandleAsync(work);
        var native = await runtime.GetRequiredService<IBookmarkStateStore>().FindAsync(run, bookmarkId)
            ?? throw new InvalidOperationException("The native create-bookmark command did not create a handle.");
        var owner = await runtime.GetRequiredService<IActivityExecutionStateStore>().FindAsync(run, activityId)
            ?? throw new InvalidOperationException("The native owner is absent.");
        var recorded = await history.FindBookmarkAsync(run, bookmarkId)
            ?? throw new InvalidOperationException("The atomic history did not retain the native handle.");
        var creation = recorded.CreatedHistoryEventId is { } creationId ? await history.FindEventAsync(run, creationId) : null;
        var expiryHead = await history.FindHeadAsync(run) ?? throw new InvalidOperationException("The native history head is absent.");
        if (owner.Status != ActivityExecutionStatus.Suspended || !owner.BookmarkIds.Contains(bookmarkId) ||
            native.ExpiresAt != expiresAt || native.CreatedAt >= expiresAt || recorded.Status != RuntimeHistoryBookmarkStatus.Active ||
            creation is not { Name: "BookmarkCreated", Authority: RuntimeHistoryAuthority.CommittedTransition } ||
            creation.ActivityExecutionId != activityId || creation.BookmarkId != bookmarkId ||
            creation.PhysicalCommitId != $"commit:{work.WorkItemId}:bookmark-created:{bookmarkId}")
            throw new InvalidOperationException("The native command did not preserve suspended ownership, expiry and recorded creation.");
        expiry = new { workflowExecutionId = run, activityExecutionId = activityId, bookmarkId, expiresAt,
            createdHistoryEventId = recorded.CreatedHistoryEventId, lastSequence = expiryHead.LastSequence, executable.Identity,
            fixtureKind = "native-legacy-wait-command", stockAuthorableExpiry = false, activityActivated = false,
            producer = WorkflowCreateBookmarkSchedulerWorkHandler.HandlerName, creation.PhysicalCommitId, creation.CheckpointId,
            fencingToken = lease.FencingToken, capturePolicy = policy };
    }
    finally
    {
        var released = await ownership.ReleaseAsync(lease);
        if (!released.Succeeded) throw new InvalidOperationException("The native fixture could not release its real writer lease.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { sourceSha = marker.SourceSha, request.Project,
        operation = "ordinary-iam-retention-native-legacy-wait", credentialsIncluded = false, historyRowsInjected = false,
        accounts, retention = new { workflowExecutionId = request.CompletedRun, purgedThrough }, expiry,
        products = new[] { Product(Assembly.GetExecutingAssembly()), Product(typeof(WorkflowExecutionState).Assembly),
            Product(typeof(RuntimeCheckpointCommitter).Assembly), Product(typeof(RuntimeEntityFrameworkCoreOptions).Assembly),
            Product(typeof(AspNetCoreIdentityUser).Assembly), Product(typeof(AspNetCoreIdentityEntityFrameworkCoreServiceCollectionExtensions).Assembly) } }, json));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.GetType().Name + ": " + exception.Message);
    return 1;
}

static bool ProcessAlive(int pid)
{
    try { return !Process.GetProcessById(pid).HasExited; }
    catch (ArgumentException) { return false; }
}
static bool ValidPermissions(string profile, string[] permissions)
{
    var expected = new HashSet<string>(["api-capabilities:read", "workflows/runtime:read"], StringComparer.Ordinal);
    if (profile is "metadata" or "payload-denied") expected.Add("workflows/activity-executions:inspect");
    if (profile == "payload-denied") expected.Add("workflows/activity-executions:inspect-values");
    return (profile is "metadata" or "payload-denied" or "denied") && expected.SetEquals(permissions);
}
static void Require(IdentityResult result)
{
    if (!result.Succeeded) throw new InvalidOperationException("The normal IAM port refused the account: " + string.Join(',', result.Errors.Select(error => error.Code)));
}
static object Product(Assembly assembly) => new { name = assembly.GetName().Name,
    informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
sealed record Owner(int SchemaVersion, string Nonce, int ControllerProcessId, string SourceSha, string Root, int BackendPid);
sealed record Request(string Nonce, string SourceSha, string Project, string CompletedRun, long ThroughSequence, string ExpiryTemplateArtifactId, Account[] Accounts);
sealed record Account(string UserName, string TenantId, string Profile, string Password, string[] Permissions);
