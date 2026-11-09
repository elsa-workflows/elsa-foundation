using System.Text.Json;
using Acme.Specialists.Design;
using Acme.Specialists.Runtime;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var artifacts = args[0];
var control = args.Length > 1 ? args[1] : "positive";
Directory.CreateDirectory(artifacts);
await using var design = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite("Data Source=" + Path.Combine(artifacts, "design.db")).Options);
await using var runtime = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=" + Path.Combine(artifacts, "runtime.db")).Options);
await using var publishing = new PublishingSnapshotReviewSqliteDbContext(new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite("Data Source=" + Path.Combine(artifacts, "publishing.db")).Options);
await design.Database.EnsureCreatedAsync(); await runtime.Database.EnsureCreatedAsync(); await publishing.Database.EnsureCreatedAsync();
var access = new Access();
var stores = new EfActivityDesignStores(design, access, null, new EfActivityManagementProjectionWriter(design, access));
var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "specialist-research-private-fixture-32-bytes", AllowEphemeralDevelopmentKey = false }));
var templates = new EfExecutableActivityTemplateStore(runtime, access, codec);
var sources = new EfWorkflowExecutableSourceReferenceStore(runtime, access, codec);
var receipts = new EfActivityPublicationReceiptStore(publishing, access);
var commit = new EfActivityPublicationCommand(receipts, templates, sources, stores, access);
var provider = new SpecialistProvider();
var compiler = new ActivityTemplateCompiler(new ActivityTemplateProviderCompilerRegistry([provider]),
    new ActivityTemplateDependencyDiscovererRegistry([provider]), stores, stores, templates,
    new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System);
var validator = new ActivityDraftValidator(new ActivityProviderRegistry([provider]), new ActivityContractAuthoringValidator(new Capabilities()), TimeProvider.System);
var publisher = new ActivityDefinitionPublisher(stores, stores, stores, stores, stores, stores, validator,
    new ActivityVersionDiffer(), compiler, access, commit, new SingleProcessLock(), new Ids(), TimeProvider.System,
    receipts, control == "missing-consumer" ? [] : [new SpecialistActivationStrategy(new UnusedClient())],
    new RuntimeDurableValueStorageDriverRegistry(control == "missing-storage" ? [] : [new JsonRuntimeDurableValueStorageDriver()]));
var now = DateTimeOffset.UtcNow;
var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { article = new { type = "string" } }, required = new[] { "article" }, additionalProperties = false });
var descriptor = new SpecialistDescriptor("copywriter-v1", "Write the supplied brief.", "fixture-model", schema);
var state = new ActivityDefinitionDraftState(SpecialistProvider.Contract(), new("acme.specialist", "1", JsonSerializer.SerializeToElement(descriptor)), new Dictionary<string, string>());
await stores.ExecuteAsync(new CreateActivityDefinitionRequest(
    new ActivityDefinition { Id = "copywriter", ActivityTypeKey = "acme.copywriter", Category = "Specialists", DisplayName = "Copywriter", CreatedAt = now, LastModifiedAt = now },
    new ActivityDefinitionAuthoringState { Id = "authoring", DefinitionId = "copywriter", ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa.design"), CreatedAt = now, LastModifiedAt = now },
    new ActivityDefinitionDraft { Id = "draft", DefinitionId = "copywriter", Revision = 1, State = state, CreatedAt = now, LastModifiedAt = now },
    new ActivityDefinitionDraftLayout { Id = "layout", DraftId = "draft", Revision = 1, CreatedAt = now, LastModifiedAt = now }));
var review = await publisher.PreflightAsync(new("draft", 1, null) { Version = "1.0.0" });
if (control != "positive")
{
    var expectedCode = control switch { "missing-consumer" => "activity.runtime.consumer-missing", "missing-storage" => "activity.runtime.storage-driver-missing", _ => throw new ArgumentException("Unknown control") };
    if (review.IsPublishable || !review.Diagnostics.Any(d => d.Code == expectedCode)
        || await design.ActivityDefinitionVersionPublications.AnyAsync() || await runtime.ExecutableActivityTemplates.AnyAsync())
        throw new InvalidOperationException("Wrong-cause control failure: " + JsonSerializer.Serialize(review));
    throw new InvalidOperationException("EXPECTED_CONTROL:" + expectedCode + ":no-publication-or-template");
}
if (!review.IsPublishable) throw new InvalidOperationException(JsonSerializer.Serialize(review));
var receipt = await publisher.PublishReviewedAsync(new("draft", 1, null, "1.0.0", review.ReviewToken, "publish-copywriter", "copywriter-v1"));
var publication = await ((IActivityDefinitionVersionPublicationStore)stores).FindAsync("copywriter-v1") ?? throw new InvalidOperationException("No publication");
var template = await templates.FindAsync(publication.TemplateId) ?? throw new InvalidOperationException("No runtime template");
await File.WriteAllTextAsync(Path.Combine(artifacts, "template.json"), JsonSerializer.Serialize(template));
await File.WriteAllTextAsync(Path.Combine(artifacts, "publication.json"), JsonSerializer.Serialize(publication));
var repeated = await publisher.PublishReviewedAsync(new("draft", 1, null, "1.0.0", review.ReviewToken, "publish-copywriter", "copywriter-v1"));
if (repeated.Status != receipt.Status) throw new InvalidOperationException("Publication idempotency changed");
// Hold identity, SemVer, schema and model fixed: this control isolates prompt behavior in the hash.
var promptOnly = new ActivityDefinitionDraft { Id = "prompt-only", DefinitionId = "copywriter", Revision = 1,
    State = state with { Provider = new("acme.specialist", "1", JsonSerializer.SerializeToElement(descriptor with { Instructions = "A changed draft." })) } };
var promptRevision = await compiler.CompileAsync(new(await ((IActivityDefinitionStore)stores).GetAsync("copywriter"), promptOnly, "copywriter-v1", "1.0.0", 0));
if (!promptRevision.IsSuccessful || promptRevision.Template!.TemplateHash == template.TemplateHash)
    throw new InvalidOperationException("Prompt-only change did not change behavior hash");
// A new candidate version must not rewrite the already published Runtime artifact.
var changed = new ActivityDefinitionDraft { Id = "changed", DefinitionId = "copywriter", Revision = 1,
    State = state with { Provider = new("acme.specialist", "1", JsonSerializer.SerializeToElement(descriptor with { AgentVersionId = "copywriter-v2", Instructions = "A changed draft." })) } };
var revised = await compiler.CompileAsync(new(await ((IActivityDefinitionStore)stores).GetAsync("copywriter"), changed, "copywriter-v2", "2.0.0", 0));
if (!revised.IsSuccessful || revised.Template!.TemplateHash == template.TemplateHash
    || !JsonElement.DeepEquals((await templates.FindAsync(publication.TemplateId))!.Root.Descriptor.Payload, JsonSerializer.SerializeToElement(descriptor)))
    throw new InvalidOperationException("Draft change rewrote immutable specialist publication");
Console.WriteLine(JsonSerializer.Serialize(new { published = publication.DefinitionVersionId, template.TemplateId, template.TemplateHash, receipt.Status, packageOnly = true, liveModel = false }));

sealed class Access : IPersistenceAccessContextAccessor, IActivityPublishingAuthorizationContext
{
    public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope("default"));
    public string? TenantId => null;
    public bool CanAccessTenant(string? tenantId) => tenantId is null;
}
sealed class Ids : IIdentityGenerator { public string Generate() => Guid.NewGuid().ToString("N"); }
sealed class Capabilities : IActivityContractCapabilityCatalog
{
    public IReadOnlyCollection<ActivityContractTypeCapability> Types => [new("String", "Text", "Primitives", "text",
        new HashSet<CollectionKind> { CollectionKind.Single }, true, true, new HashSet<string> { "elsa.json" })];
}
// One author process; concurrency ownership is probed separately, never inferred from this fixture lock.
sealed class SingleProcessLock : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());
    sealed class Handle : IDistributedSynchronizationHandle { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() {} public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
sealed class UnusedClient : Microsoft.Extensions.AI.IChatClient
{
    public void Dispose() {}
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Author must never call model");
    public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Author must never call model");
}
