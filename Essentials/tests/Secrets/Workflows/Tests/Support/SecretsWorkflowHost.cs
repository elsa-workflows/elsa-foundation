using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Features;
using Elsa.Secrets.Options;
using Elsa.Secrets.Services;
using Elsa.Secrets.Workflows.Features;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>Which runtime stores a <see cref="SecretsWorkflowHost"/> runs on.</summary>
public enum SecretsWorkflowHostStore
{
    /// <summary>The EF runtime stores on a SQLite file, which keep each persistence scope's rows apart.</summary>
    Sqlite,

    /// <summary>
    /// The in-memory runtime stores. Unlike the EF stores, they keep an instance whose tenant differs from the partition
    /// it runs under, so the activation-time tenant check is the only thing that can refuse it.
    /// </summary>
    InMemory
}

/// <summary>
/// An embedded workflow runtime host for the Secrets bridge (spec 188, slice 4), dedicated to one tenant: the in-process
/// runtime, the Secrets module with its encrypted store and, unless a test leaves it out, the <c>SecretsWorkflows</c>
/// bridge.
/// </summary>
/// <remarks>
/// <para>
/// <b>One tenant per host.</b> The host's persistence scope is its tenant, composed with
/// <see cref="PersistenceCoreServiceCollectionExtensions.AddPersistenceCore"/> ahead of the runtime, as a host
/// dedicated to a tenant composes it, and every workflow it runs starts in that partition. The tenant handed to the
/// resolver is the execution partition read at activation. This host's drain hands the scheduler work handlers no
/// ambient services, so they activate in a fresh DI scope that carries the host's persistence scope, and that scope is
/// the partition read. Two tenants are therefore proven with two hosts, which can share one Secrets repository
/// (<see cref="CreateAsync"/>). A drain whose dispatch options carry ambient services hands the handlers those instead;
/// that path was not verified for several tenants.
/// </para>
/// <para>
/// The Secrets module's own resolver is wrapped in a recording one, so a test sees each tenant and reference the bridge
/// handed to the Secrets module. A store whose reads always fail (<see cref="UnavailableSecretStore"/>) is composed
/// beside the built-in ones, and the Secrets resolver reads the secret repository through one a test can take offline
/// (<see cref="TakeSecretRepositoryOffline"/>) or make return a damaged row (<see cref="DamageSecretRepository"/>).
/// </para>
/// </remarks>
public sealed class SecretsWorkflowHost : IAsyncDisposable
{
    public const string AlphaTenant = "tenant-alpha";
    public const string BetaTenant = "tenant-beta";
    public const string ReferenceName = "payments.api-key";
    public const string NodeId = "node-secret";
    public const string ChildNodeId = "node-child";
    private const string ChildSlotName = "Test.Children";
    private const string EncryptionKey = "secrets-workflows-tests-encryption-key";
    private const string RecoverySigningKey = "secrets-workflows-tests-recovery-signing-key-32";
    private const string HierarchySigningKey = "secrets-workflows-tests-hierarchy-signing-key-32";
    private const string RequestedBy = "secrets-workflows-tests";
    private const int SqliteBusy = 5;

    private readonly ServiceProvider _services;
    private readonly string? _databasePath;
    private readonly SecretRepositoryFailure _repositoryFailure;
    private int _artifacts;

    private SecretsWorkflowHost(
        string tenantId,
        ServiceProvider services,
        string? databasePath,
        SecretValueRecorder values,
        SecretResolutionRecorder resolutions,
        SecretRepositoryFailure repositoryFailure)
    {
        TenantId = tenantId;
        _services = services;
        _databasePath = databasePath;
        Values = values;
        Resolutions = resolutions;
        _repositoryFailure = repositoryFailure;
    }

    /// <summary>The tenant this host is dedicated to: its persistence scope and the partition its workflows run under.</summary>
    public string TenantId { get; }

    public IServiceProvider Services => _services;

    /// <summary>What the test activities were hydrated with.</summary>
    public SecretValueRecorder Values { get; }

    /// <summary>Every tenant and reference handed to the Secrets module's resolver.</summary>
    public SecretResolutionRecorder Resolutions { get; }

    /// <summary>
    /// Builds a host dedicated to <paramref name="tenantId"/> on <paramref name="store"/>. Without
    /// <paramref name="composeBridge"/> the host composes the Secrets module but no <see cref="IRuntimeSecretResolver"/>,
    /// as a host that forgot the bridge does. A host built with <paramref name="sharedSecrets"/> reads and writes that
    /// host's secret repository, so two tenants' hosts see one secret store holding both tenants' secrets.
    /// <paramref name="configure"/> runs last, after the bridge is composed.
    /// </summary>
    public static async Task<SecretsWorkflowHost> CreateAsync(
        string tenantId = AlphaTenant,
        SecretsWorkflowHostStore store = SecretsWorkflowHostStore.Sqlite,
        bool composeBridge = true,
        SecretsWorkflowHost? sharedSecrets = null,
        Action<IServiceCollection>? configure = null)
    {
        var values = new SecretValueRecorder();
        var resolutions = new SecretResolutionRecorder();
        var repositoryFailure = new SecretRepositoryFailure();
        var databasePath = store == SecretsWorkflowHostStore.Sqlite
            ? Path.Join(Path.GetTempPath(), $"elsa-secrets-workflows-{Guid.NewGuid():N}.db")
            : null;
        var services = new ServiceCollection();
        services.AddLogging();
        // The host's persistence scope, registered ahead of the runtime, whose own default registration then yields to it.
        services.AddPersistenceCore(tenantId);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new ActivitiesPrimitivesFeature().ConfigureServices(services);
        if (databasePath is not null)
            services
                .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = RecoverySigningKey,
                    HierarchyCursorSigningKey = HierarchySigningKey
                })
                .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");

        if (sharedSecrets is not null)
            services.AddSingleton(sharedSecrets.Services.GetRequiredService<ISecretRepository>());
        new SecretsFeature().ConfigureServices(services);
        services.Configure<SecretsOptions>(options => options.EncryptionKey = EncryptionKey);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretStore, UnavailableSecretStore>());
        services.AddScoped(provider => ActivatorUtilities.CreateInstance<DefaultSecretValueResolver>(
            provider,
            new FailingSecretRepository(provider.GetRequiredService<ISecretRepository>(), repositoryFailure)));
        services.Replace(ServiceDescriptor.Scoped<ISecretValueResolver>(provider =>
            new RecordingSecretValueResolver(resolutions, provider.GetRequiredService<DefaultSecretValueResolver>())));
        if (composeBridge)
            new SecretsWorkflowsFeature().ConfigureServices(services);
        services.AddSingleton(values);
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        // A plain host has no CShells activation phase: install the runtime schema and register the activity types here.
        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        await ActivatorUtilities.CreateInstance<RegisterActivityTypesStartupTask>(provider).ExecuteAsync(CancellationToken.None);

        return new SecretsWorkflowHost(tenantId, provider, databasePath, values, resolutions, repositoryFailure);
    }

    /// <summary>A secret reference authored as the Studio secret picker writes it.</summary>
    public static ArgumentValue Secret(string name = ReferenceName, string? typeName = SecretTypeNames.Text)
    {
        var reference = new JsonObject { ["name"] = name };
        if (typeName is not null)
            reference["typeName"] = typeName;
        return new ArgumentValue(JsonSerializer.SerializeToElement(reference), SecretExpressionTypes.Secret);
    }

    /// <summary>
    /// A workflow whose root is <paramref name="activityType"/> with its <c>Text</c> input bound to
    /// <paramref name="secret"/>, compiled by the publish compiler. A structural parent gets one probe child, and a
    /// <see cref="SecretWaitingActivity"/> its resume target. Each call is a new artifact.
    /// </summary>
    private WorkflowExecutable SecretWorkflow(Type activityType, ArgumentValue secret)
    {
        const string inputName = nameof(SecretReadingActivity.Text);
        var compiler = new RuntimeInputBindingCompiler(Services.GetRequiredService<IWellKnownTypeRegistry>());
        var root = ClrNode(
            NodeId,
            activityType,
            new Dictionary<string, RuntimeInputBinding> { [inputName] = compiler.Compile(NodeId, Input(activityType, inputName), secret) },
            typeof(SecretParentActivityBase).IsAssignableFrom(activityType)
                ? [new ExecutableChildSlot(ChildSlotName, [ClrNode(ChildNodeId, typeof(CompletingChildActivity), new Dictionary<string, RuntimeInputBinding>(), null)])]
                : null);
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(NodeId, SecretWaitingActivity.ResumeTargetKey);
        var artifact = $"secret-workflow-{Interlocked.Increment(ref _artifacts)}";

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(artifact, $"{artifact}-definition", $"{artifact}-version", "1.0.0", $"sha256:{artifact}"),
            rootActivity: root,
            resumeTargets: activityType == typeof(SecretWaitingActivity)
                ? new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
                {
                    [resumeTargetId] = new(resumeTargetId, NodeId, "ResumeAsync", new Dictionary<string, string>(), SecretWaitingActivity.ResumeTargetKey)
                }
                : new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal),
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    public Task CreateSecretAsync(string tenantId, string name, string value, string typeName = SecretTypeNames.Text, DateTimeOffset? expiresAt = null) =>
        WithSecretManagerAsync(manager => manager.CreateAsync(
            tenantId,
            new CreateSecretRequest { Name = name, Value = value, TypeName = typeName, ExpiresAt = expiresAt }));

    public Task RotateSecretAsync(string tenantId, string name, string value) =>
        WithSecretManagerAsync(manager => manager.RotateAsync(tenantId, name, new RotateSecretRequest { Value = value }));

    public Task RevokeSecretAsync(string tenantId, string name) =>
        WithSecretManagerAsync(manager => manager.RevokeAsync(tenantId, name));

    public Task DeleteSecretAsync(string tenantId, string name) =>
        WithSecretManagerAsync(manager => manager.DeleteAsync(tenantId, name));

    /// <summary>
    /// Stores an active secret whose payload lives in <see cref="UnavailableSecretStore"/>, written straight to the
    /// repository because no secret type accepts that store.
    /// </summary>
    public async Task SeedUnavailableSecretAsync(string tenantId, string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<ISecretRepository>().TryAddAsync(new Secret
        {
            Id = $"{tenantId}:{name}",
            TenantId = tenantId,
            Name = name,
            DisplayName = name,
            TypeName = SecretTypeNames.Text,
            StoreName = UnavailableSecretStore.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            Versions = [new SecretVersion { Version = 1, CreatedAt = DateTimeOffset.UtcNow, Payload = new SecretPayload() }]
        });
        if (!created)
            throw new InvalidOperationException($"Secret '{name}' already exists in tenant '{tenantId}'.");
    }

    /// <summary>
    /// From now on, every read the Secrets resolver makes from the secret repository fails as a database another
    /// connection holds does (SQLite's busy error, an outage that clears on its own), with
    /// <see cref="UnavailableSecretStore.StoreDetail"/>.
    /// </summary>
    public void TakeSecretRepositoryOffline() => _repositoryFailure.Failure = new SqliteException(UnavailableSecretStore.StoreDetail, SqliteBusy);

    /// <summary>
    /// From now on, every read the Secrets resolver makes from the secret repository fails as the EF secret repository
    /// does for a row whose document names another tenant than its key.
    /// </summary>
    public void DamageSecretRepository() =>
        _repositoryFailure.Failure = new InvalidOperationException("Secret document tenant does not match its storage identity.");

    /// <summary>
    /// Compiles <see cref="SecretWorkflow"/> for <paramref name="activityType"/>, bound to <paramref name="secret"/> or
    /// else to <see cref="ReferenceName"/>, and publishes it in this host's tenant.
    /// </summary>
    public async Task<WorkflowExecutable> PublishSecretWorkflowAsync(Type activityType, ArgumentValue? secret = null)
    {
        var executable = SecretWorkflow(activityType, secret ?? Secret());
        await PublishAsync(executable);
        return executable;
    }

    /// <summary>Publishes a new <see cref="SecretWorkflow"/> as <see cref="PublishSecretWorkflowAsync"/> does, and starts it as <see cref="StartAsync"/> does.</summary>
    public async Task<WorkflowExecutionRun> RunSecretWorkflowAsync(
        Type activityType,
        string workflowExecutionId,
        ArgumentValue? secret = null,
        string? instanceTenantId = null) =>
        await StartAsync(await PublishSecretWorkflowAsync(activityType, secret), workflowExecutionId, instanceTenantId);

    /// <summary>Saves <paramref name="executable"/> with a live published reference in this host's tenant.</summary>
    private async Task PublishAsync(WorkflowExecutable executable)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(SourceReference(executable.Identity));
    }

    /// <summary>
    /// Starts the published <paramref name="executable"/> under this host's tenant, through the production start
    /// dispatcher, and returns the run once its drain is done. The instance records <paramref name="instanceTenantId"/>,
    /// the host's tenant unless a test stores a disagreeing one.
    /// </summary>
    public async Task<WorkflowExecutionRun> StartAsync(WorkflowExecutable executable, string workflowExecutionId, string? instanceTenantId = null)
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var start = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(new WorkflowExecutionStartDispatchRequest(
                artifactId: executable.Identity.ArtifactId,
                requestedBy: RequestedBy,
                workflowExecutionId: workflowExecutionId,
                idempotencyKey: $"start:{workflowExecutionId}",
                metadata: null,
                variables: null,
                inputs: null,
                stimulusInput: null,
                triggerNodeId: null,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: new WorkflowExecutableSourceSelection(SourceReferenceId(executable.Identity)),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: null,
                tenantId: instanceTenantId ?? TenantId,
                partition: new WorkflowExecutionPartition(TenantId),
                authority: new WorkflowExecutionAuthoritySnapshot(systemIdentity: RequestedBy, rootInitiator: RequestedBy)));
            if (start.CommandDispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
                throw new InvalidOperationException($"Start was not accepted ({start.CommandDispatch.Status}): {start.CommandDispatch.Reason}");
        }

        return await ReadRunAsync(workflowExecutionId);
    }

    /// <summary>Delivers the stimulus a <see cref="SecretWaitingActivity"/> waits for, through the production resume dispatcher.</summary>
    public async Task<WorkflowExecutionRun> ResumeAsync(string workflowExecutionId)
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new BookmarkResumeDispatchRequest(
                workflowExecutionId,
                SecretWaitingActivity.StimulusType,
                SecretWaitingActivity.StimulusHash,
                input: JsonSerializer.SerializeToElement(new SecretWaitTrigger(true))));
            if (resumed.Status != BookmarkResumeDispatchStatus.Dispatched)
                throw new InvalidOperationException($"Resume was not dispatched ({resumed.Status}): {resumed.Reason}");
        }

        return await ReadRunAsync(workflowExecutionId);
    }

    public async Task<WorkflowExecutionRun> ReadRunAsync(string workflowExecutionId)
    {
        await using var scope = Services.CreateAsyncScope();
        return new WorkflowExecutionRun(
            await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId),
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId));
    }

    public async Task<IReadOnlyCollection<IncidentState>> ListIncidentsAsync(string workflowExecutionId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(workflowExecutionId);
    }

    /// <summary>
    /// Asserts that no row of this host's runtime database holds any of <paramref name="values"/>. Every table is read,
    /// so every row family the run wrote is covered (workflow execution state, activity states, bookmarks, durable
    /// values, inspection projections, checkpoint commits, the outbox and incidents among them), whichever store wrote it. As
    /// the positive control, the same rows must hold <see cref="ReferenceName"/>, which the persisted withheld envelope
    /// names and the EF stores write only in an encoded form, so a scan that cannot read the encoded forms fails here
    /// instead of passing vacuously.
    /// </summary>
    public async Task AssertNotPersistedAsync(params string[] values)
    {
        var persisted = await ReadPersistedTextAsync();
        Assert.Contains(persisted, text => text.Contains(ReferenceName, StringComparison.Ordinal));
        foreach (var value in values)
            Assert.DoesNotContain(persisted, text => text.Contains(value, StringComparison.Ordinal));
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (_databasePath is null)
            return;

        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(path);
    }

    /// <summary>
    /// Every text and binary cell of this host's runtime database, a binary one read as UTF-8 and as UTF-16LE, as stored
    /// and as the EF runtime stores decode it: a compressed payload frame (<see cref="EfPayloadCodec"/>), and, within it,
    /// every JSON string and property name, each of which <c>RuntimeArtifactJson</c> writes as Base64 of its UTF-16 code
    /// units (<see cref="EfRelationalIdentity.Encode"/>).
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadPersistedTextAsync()
    {
        if (_databasePath is null)
            throw new InvalidOperationException("Only a host on the SQLite runtime stores has a database to scan.");

        var texts = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        foreach (var table in await ReadColumnAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table'"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\"";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    switch (reader.GetValue(column))
                    {
                        case string text:
                            AddDecoded(texts, text);
                            break;
                        case byte[] bytes:
                            // A binary cell is read as each encoding a store writes text to bytes in, then decoded as
                            // a text cell is, so a value leaked into a BLOB column is found too.
                            AddDecoded(texts, Encoding.UTF8.GetString(bytes));
                            AddDecoded(texts, Encoding.Unicode.GetString(bytes));
                            break;
                    }
                }
            }
        }

        return texts;
    }

    private static async Task<IReadOnlyList<string>> ReadColumnAsync(SqliteConnection connection, string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }

    /// <summary>Adds <paramref name="text"/> and every form it decodes to.</summary>
    private static void AddDecoded(List<string> texts, string text)
    {
        texts.Add(text);
        if (EfPayloadCodec.IsFrame(text))
        {
            AddDecoded(texts, EfPayloadCodec.Decode(text)!);
            return;
        }

        if (text.Length > 0 && text.Length % 4 == 0 && Convert.TryFromBase64String(text, new byte[text.Length], out var length) && length % sizeof(char) == 0)
            AddDecoded(texts, EfRelationalIdentity.Decode(text));
        if (TryParseJson(text) is { } document)
        {
            using (document)
                AddJsonStrings(texts, document.RootElement);
        }
    }

    private static void AddJsonStrings(List<string> texts, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddDecoded(texts, property.Name);
                    AddJsonStrings(texts, property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    AddJsonStrings(texts, item);
                break;
            case JsonValueKind.String:
                AddDecoded(texts, element.GetString()!);
                break;
        }
    }

    private static JsonDocument? TryParseJson(string text)
    {
        if (!text.StartsWith('{') && !text.StartsWith('['))
            return null;
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WithSecretManagerAsync<T>(Func<ISecretManager, ValueTask<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ISecretManager>());
    }

    /// <summary>A node of the CLR <paramref name="activityType"/> with its contract pinned, as publish pins it.</summary>
    private static ExecutableNode ClrNode(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(activityType);
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: ClrActivityContractTestBuilder.CompleteInputBindings(contract, inputBindings),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            activityContract: contract);
    }

    /// <summary>The input as the catalog declares it for <paramref name="activityType"/>'s CLR property.</summary>
    private static InputDefinition Input(Type activityType, string name) =>
        new(
            name,
            name,
            TypeReferenceFactory.FromClrType(activityType.GetProperty(name)!.PropertyType, TypeAliasConvention.CanonicalAlias),
            StorageDriverType: null,
            DisplayName: name,
            Category: null,
            IsNullable: true);

    private string SourceReferenceId(WorkflowExecutableIdentity identity) => $"{TenantId}:{identity.ArtifactId}";

    private WorkflowExecutableSourceReference SourceReference(WorkflowExecutableIdentity identity)
    {
        var id = SourceReferenceId(identity);
        return new WorkflowExecutableSourceReference(
            SourceReferenceId: id,
            ArtifactId: identity.ArtifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: identity.DefinitionVersionId,
            SourceVersion: identity.ArtifactVersion,
            DefinitionId: identity.DefinitionId,
            DefinitionVersionId: identity.DefinitionVersionId,
            ArtifactVersion: identity.ArtifactVersion,
            CreatedAt: WorkflowExecutionHarness.Timestamp,
            PublishedAt: WorkflowExecutionHarness.Timestamp,
            Scope: WorkflowExecutableReferenceScope.Published,
            ExpiresAt: null,
            ActivationId: $"publication-{id}",
            SlotId: $"slot-{id}");
    }
}

/// <summary>One request handed to the Secrets module's resolver.</summary>
public sealed record RecordedSecretResolution(string TenantId, SecretReference Reference, CancellationToken CancellationToken);

/// <summary>Records every request handed to the Secrets module's resolver.</summary>
public sealed class SecretResolutionRecorder
{
    private readonly List<RecordedSecretResolution> _requests = [];

    public IReadOnlyList<RecordedSecretResolution> Requests
    {
        get
        {
            lock (_requests)
                return _requests.ToArray();
        }
    }

    public void Add(RecordedSecretResolution request)
    {
        lock (_requests)
            _requests.Add(request);
    }
}

/// <summary>
/// Records each request in <paramref name="recorder"/>, then answers it through <paramref name="inner"/>, the Secrets
/// module's own resolver, or, without one, with <see cref="Answer"/>.
/// </summary>
public sealed class RecordingSecretValueResolver(SecretResolutionRecorder recorder, ISecretValueResolver? inner = null) : ISecretValueResolver
{
    public ResolvedSecret? Answer { get; set; } = ResolvedSecret.Success("value", new SecretMetadata());

    public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        recorder.Add(new(tenantId, reference, cancellationToken));
        return inner?.ResolveAsync(tenantId, reference, cancellationToken) ?? ValueTask.FromResult(Answer!);
    }
}

/// <summary>What every read of the secret repository the Secrets resolver reads throws, if anything.</summary>
public sealed class SecretRepositoryFailure
{
    public Exception? Failure { get; set; }
}

/// <summary>
/// The secret repository as the Secrets resolver reads it: <paramref name="inner"/>, except that while
/// <paramref name="failure"/> holds an exception every read throws it.
/// </summary>
internal sealed class FailingSecretRepository(ISecretRepository inner, SecretRepositoryFailure failure) : ISecretRepository
{
    public ValueTask<Secret?> FindAsync(string tenantId, string normalizedName, CancellationToken cancellationToken = default) =>
        failure.Failure is { } exception
            ? throw exception
            : inner.FindAsync(tenantId, normalizedName, cancellationToken);

    public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default) =>
        inner.TryAddAsync(secret, cancellationToken);

    public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default) =>
        inner.SaveAsync(secret, cancellationToken);

    public ValueTask<SecretRepositoryPage> ListPageAsync(string tenantId, SecretRepositoryListRequest request, CancellationToken cancellationToken = default) =>
        inner.ListPageAsync(tenantId, request, cancellationToken);
}

/// <summary>
/// A secret store whose reads always fail as a store that cannot be reached does, with a message carrying store-private
/// detail.
/// </summary>
public sealed class UnavailableSecretStore : ISecretStore
{
    public const string Name = "unavailable";

    /// <summary>What the store's failure says, as a real one can: a store host name.</summary>
    public const string StoreDetail = "store-detail-sentinel read from secrets-db.internal failed";

    public SecretStoreDescriptor Descriptor { get; } = new(Name, "Unavailable", "A store whose reads always fail.", SecretStoreCapabilities.Read, IsReadOnly: true);

    public ValueTask<SecretPayload?> ReadAsync(SecretReadContext context, CancellationToken cancellationToken = default) =>
        throw new IOException(StoreDetail);

    public ValueTask<SecretPayload> WriteAsync(SecretWriteContext context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask DeleteAsync(SecretDeleteContext context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<SecretTestResult> TestAsync(SecretTestContext context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
