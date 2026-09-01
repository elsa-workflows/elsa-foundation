using System.Data;
using System.Data.Common;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Temporary EF-only oracle for the #646 Secret workload. The EF model and provider types intentionally
/// live in this executable adapter host; the benchmark workload references only public Secret contracts.
/// </summary>
internal sealed class EfSecretRepositoryAdapter : IBenchmarkAdapter, ISecretCreateReadListWorkloadAdapter
{
    internal const string PhysicalForm = "entity-type-specific-physical-tables";

    private readonly RunRequest request;
    private readonly string connectionString;
    private readonly string outputDirectory;
    private readonly string persistenceScope;
    private readonly EfRoundTripObserver observer;
    private readonly SecretProviderConcurrencyProbe concurrencyProbe = new();
    private DbContextOptions<SecretDbContext>? options;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;

    internal EfSecretRepositoryAdapter(RunRequest request, string connectionString, string outputDirectory)
    {
        this.request = request;
        this.connectionString = connectionString;
        this.outputDirectory = outputDirectory;
        persistenceScope = SecretStorageScope.For(request);
        observer = new EfRoundTripObserver(concurrencyProbe);
    }

    public IProviderRoundTripObserver RoundTripObserver => observer;

    internal EfRoundTripObserver CommandObserver => observer;

    internal SecretProviderConcurrencyEvidence? ConcurrencyEvidence { get; private set; }

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The secret-create-read-list operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (observedProvider is not null)
            return;

        if (!string.Equals(request.Provider, "sqlite", StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The temporary EF Secret comparator only supports sqlite; received '{request.Provider}'.");

        var observed = await ProviderProbe.ReadAsync("sqlite", connectionString, cancellationToken);
        options = BuildOptions(connectionString, observer);
        await using var context = new SecretDbContext(options);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        observedProvider = observed;
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        RequirePrepared();
        var workload = new SecretCreateReadListWorkload();
        var result = await workload.ExecuteAsync(this, cancellationToken);
        var concurrency = RequireConcurrencyEvidence();
        operations = (await workload.PrepareMeasuredOperationsAsync(this, cancellationToken))
            .Select(operation => (IBenchmarkOperation)new BenchmarkOperation(operation))
            .ToArray();
        var provider = observedProvider!;
        var staged = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);

        if (staged.ProviderConcurrency != concurrency)
            throw new PerformanceContractException(
                "The staged EF Secret native evidence does not match the live command-concurrency proof.");
        return new CorrectnessEvidence(
            result.ResultDigest,
            provider.Version,
            provider.Topology,
            provider.Configuration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                staged.Routes)
            {
                ProviderConcurrency = concurrency
            });
    }

    public ValueTask<SecretCreateReadListScopes> OpenIsolatedScopesAsync(
        CancellationToken cancellationToken = default)
    {
        RequirePrepared();
        cancellationToken.ThrowIfCancellationRequested();
        // Each client creates a fresh DbContext for every public repository call. This mirrors the
        // independent connections used by real request handlers and makes the contender test exercise
        // the SQLite unique index rather than an in-memory lock.
        return new(new SecretCreateReadListScopes(
            new Client(OpenPublicRepository(), concurrencyProbe, observer),
            new Client(OpenPublicRepository(), concurrencyProbe, observer)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static DbContextOptions<SecretDbContext> BuildOptions(
        string connectionString,
        EfRoundTripObserver observer)
    {
        var settings = new SqliteConnectionStringBuilder(connectionString)
        {
            DefaultTimeout = 30
        };
        return new DbContextOptionsBuilder<SecretDbContext>()
            .UseSqlite(settings.ConnectionString)
            .AddInterceptors(observer)
            .Options;
    }

    private async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken)
    {
        ValidateIdentity(secret.TenantId, secret.Name);
        await using var context = new SecretDbContext(Options);
        var storageSecret = SecretStorageScope.ToStorage(secret, persistenceScope);
        context.Secrets.Add(SecretEntity.FromSecret(storageSecret));
        // The temporary SQLite oracle deliberately lets each independent client reach its native
        // INSERT command before SQLite arbitrates the unique-key race. EF's default implicit write
        // transaction takes SQLite's writer reservation before CommandExecuting and would therefore
        // serialize the contenders before the command seam that this benchmark must prove.
        if (string.Equals(secret.Id, SecretCreateReadListWorkload.WinnerSecretId, StringComparison.Ordinal))
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return false;
        }
    }

    private async ValueTask<Secret?> FindAsync(
        string tenantId,
        string normalizedName,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(tenantId, normalizedName);
        await using var context = new SecretDbContext(Options);
        var physicalTenantId = SecretStorageScope.PhysicalTenant(tenantId, persistenceScope);
        var entity = await context.Secrets
            .AsNoTracking()
            .Include(secret => secret.Versions)
            .SingleOrDefaultAsync(
                secret => secret.TenantId == physicalTenantId && secret.Name == normalizedName,
                cancellationToken);
        return entity is null ? null : SecretStorageScope.ToLogical(entity.ToSecret(), tenantId, persistenceScope);
    }

    private async ValueTask<SecretRepositoryPage> ListPageAsync(
        string tenantId,
        SecretRepositoryListRequest request,
        CancellationToken cancellationToken)
    {
        ValidateTenant(tenantId);
        await using var context = new SecretDbContext(Options);
        var physicalTenantId = SecretStorageScope.PhysicalTenant(tenantId, persistenceScope);
        var query = context.Secrets
            .AsNoTracking()
            .Where(secret => secret.TenantId == physicalTenantId);

        if (request.Search is not null)
            query = query.Where(secret => secret.Name.Contains(request.Search) || secret.DisplayName.Contains(request.Search));
        if (request.TypeName is not null)
            query = query.Where(secret => secret.TypeName == request.TypeName);
        if (request.TypeNames.Count > 0)
            query = query.Where(secret => request.TypeNames.Contains(secret.TypeName));
        if (request.StoreName is not null)
            query = query.Where(secret => secret.StoreName == request.StoreName);
        if (request.StoreNames.Count > 0)
            query = query.Where(secret => request.StoreNames.Contains(secret.StoreName));
        if (request.Scope is not null)
            query = query.Where(secret => secret.Scope == request.Scope);
        if (request.Status is not null)
            query = query.Where(secret => secret.Status == request.Status);
        if (request.ExcludedStatus is not null)
            query = query.Where(secret => secret.Status != request.ExcludedStatus);
        if (request.ActiveOnly)
        {
            var now = request.Now!.Value;
            query = query.Where(secret =>
                secret.Status == SecretStatus.Active &&
                secret.Versions.Any(version =>
                    version.Status == SecretStatus.Active &&
                    (version.ExpiresAt == null || version.ExpiresAt > now)));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(secret => secret.Name)
            .ThenBy(secret => secret.Id)
            .Include(secret => secret.Versions)
            .AsSplitQuery()
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);
        return new SecretRepositoryPage(
            items.Select(secret => SecretStorageScope.ToLogical(secret.ToSecret(), tenantId, persistenceScope)).ToArray(),
            totalCount);
    }

    private async ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken)
    {
        ValidateIdentity(secret.TenantId, secret.Name);
        await using var context = new SecretDbContext(Options);
        var storageSecret = SecretStorageScope.ToStorage(secret, persistenceScope);
        var existing = await context.Secrets
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.Id == storageSecret.Id, cancellationToken);
        if (existing is not null)
            context.Secrets.Remove(existing);
        context.Secrets.Add(SecretEntity.FromSecret(storageSecret));
        await context.SaveChangesAsync(cancellationToken);
    }

    private void RequirePrepared()
    {
        if (observedProvider is null)
            throw new PerformanceContractException(
                "The EF Secret comparator has no provider handshake; PrepareAsync must run first.");
    }

    private DbContextOptions<SecretDbContext> Options => options ?? throw new PerformanceContractException(
        "The EF Secret comparator has no EF options; PrepareAsync must run first.");

    internal string PhysicalTenantId(string tenantId) =>
        SecretStorageScope.PhysicalTenant(tenantId, persistenceScope);

    internal SecretProviderConcurrencyEvidence RequireConcurrencyEvidence() =>
        ConcurrencyEvidence ??= concurrencyProbe.RequireProven(
            observer.ContenderPhysicalConnectionCount,
            requireDistinctPhysicalConnections: true);

    internal ISecretRepository OpenPublicRepository()
    {
        RequirePrepared();
        return new EfSecretRepository(this);
    }

    private static void ValidateIdentity(string tenantId, string normalizedName)
    {
        ValidateTenant(tenantId);
        SecretNameConstraints.Validate(normalizedName);
    }

    private static void ValidateTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (tenantId.Length > 256)
            throw new ArgumentException("A secret tenant ID cannot exceed 256 characters.", nameof(tenantId));
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };

    private sealed class EfSecretRepository(EfSecretRepositoryAdapter adapter) : ISecretRepository
    {
        public ValueTask<Secret?> FindAsync(
            string tenantId,
            string normalizedName,
            CancellationToken cancellationToken = default) =>
            adapter.FindAsync(tenantId, normalizedName, cancellationToken);

        public ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default) =>
            adapter.TryAddAsync(secret, cancellationToken);

        public ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default) =>
            adapter.SaveAsync(secret, cancellationToken);

        public ValueTask<SecretRepositoryPage> ListPageAsync(
            string tenantId,
            SecretRepositoryListRequest request,
            CancellationToken cancellationToken = default) =>
            adapter.ListPageAsync(tenantId, request, cancellationToken);
    }

    private sealed class Client(
        ISecretRepository repository,
        SecretProviderConcurrencyProbe concurrencyProbe,
        EfRoundTripObserver observer) : ISecretCreateReadListClient
    {
        public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
        {
            var lease = await concurrencyProbe.EnterAsync(
                this,
                secret,
                observer,
                cancellationToken);
            using var providerCall = lease?.BeginProviderCall();
            try
            {
                return await repository.TryAddAsync(secret, cancellationToken);
            }
            finally
            {
                lease?.Complete(observer.Snapshot());
            }
        }

        public ValueTask<Secret?> FindAsync(
            string tenantId,
            string normalizedName,
            CancellationToken cancellationToken = default) =>
            repository.FindAsync(tenantId, normalizedName, cancellationToken);

        public ValueTask<SecretRepositoryPage> ListPageAsync(
            string tenantId,
            SecretRepositoryListRequest request,
            CancellationToken cancellationToken = default) =>
            repository.ListPageAsync(tenantId, request, cancellationToken);
    }

    private sealed class BenchmarkOperation(ISecretCreateReadListWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}

internal sealed class EfRoundTripObserver(SecretProviderConcurrencyProbe concurrencyProbe) : DbCommandInterceptor, IProviderRoundTripObserver
{
    private long count;
    private readonly List<EfCommandSnapshot> commands = [];
    private readonly HashSet<DbConnection> contenderConnections = new(ReferenceEqualityComparer.Instance);

    public string Provider => "sqlite";
    public string Instrumentation => "ef-core:DbCommandInterceptor";
    public bool IsExact => true;
    public long Snapshot() => Interlocked.Read(ref count);

    internal IReadOnlyList<EfCommandSnapshot> Commands
    {
        get
        {
            lock (commands)
                return commands.ToArray();
        }
    }

    internal void ClearCommands()
    {
        lock (commands)
            commands.Clear();
    }

    internal int ContenderPhysicalConnectionCount
    {
        get
        {
            lock (commands)
                return contenderConnections.Count;
        }
    }

    private void Observe(DbCommand command)
    {
        var contender = IsContenderCommand(command);
        if (contender)
            concurrencyProbe.ProviderCommandStarting();
        Interlocked.Increment(ref count);
        lock (commands)
        {
            commands.Add(new EfCommandSnapshot(
                command.CommandText,
                command.Parameters.Cast<DbParameter>()
                    .Select(parameter => new EfParameterSnapshot(
                        parameter.ParameterName,
                        SnapshotValue(parameter.Value),
                        parameter.DbType,
                        parameter.Size))
                    .ToArray()));
            if (contender && command.Connection is not null)
                contenderConnections.Add(command.Connection);
        }
    }

    private static bool IsContenderCommand(DbCommand command) =>
        command.CommandText.Contains("INSERT INTO \"Secrets\"", StringComparison.OrdinalIgnoreCase) &&
        command.Parameters.Cast<DbParameter>().Any(parameter =>
            Convert.ToString(parameter.Value, System.Globalization.CultureInfo.InvariantCulture) is { } value &&
            (string.Equals(value, SecretCreateReadListWorkload.WinnerSecretId, StringComparison.Ordinal) ||
             value.EndsWith(':' + SecretCreateReadListWorkload.WinnerSecretId, StringComparison.Ordinal)));

    private static object? SnapshotValue(object? value) => value switch
    {
        DBNull => null,
        byte[] bytes => bytes.ToArray(),
        _ => value
    };

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Observe(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Observe(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Observe(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Observe(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Observe(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Observe(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}

internal sealed record EfCommandSnapshot(
    string CommandText,
    IReadOnlyList<EfParameterSnapshot> Parameters);

internal sealed record EfParameterSnapshot(
    string Name,
    object? Value,
    DbType DbType,
    int Size);

internal sealed class SecretDbContext(DbContextOptions<SecretDbContext> options) : DbContext(options)
{
    public DbSet<SecretEntity> Secrets => Set<SecretEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var secret = modelBuilder.Entity<SecretEntity>();
        secret.HasKey(entity => entity.Id);
        secret.HasIndex(entity => new { entity.TenantId, entity.Name }).IsUnique();
        secret.HasIndex(entity => new { entity.TenantId, entity.Status, entity.Name });
        secret.Property(entity => entity.Status).HasConversion<string>();
        secret.HasMany(entity => entity.Versions)
            .WithOne(version => version.Secret)
            .HasForeignKey(version => version.SecretId)
            .OnDelete(DeleteBehavior.Cascade);

        var version = modelBuilder.Entity<SecretVersionEntity>();
        version.HasKey(entity => new { entity.SecretId, entity.Version });
        version.Property(entity => entity.Status).HasConversion<string>();
    }
}

internal sealed class SecretEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string TypeName { get; set; } = SecretTypeNames.Text;
    public string StoreName { get; set; } = SecretStoreNames.Encrypted;
    public string? Scope { get; set; }
    public string TagsJson { get; set; } = "[]";
    public SecretStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<SecretVersionEntity> Versions { get; set; } = [];

    public static SecretEntity FromSecret(Secret secret) => new()
    {
        Id = secret.Id,
        TenantId = secret.TenantId,
        Name = secret.Name,
        DisplayName = secret.DisplayName,
        Description = secret.Description,
        TypeName = secret.TypeName,
        StoreName = secret.StoreName,
        Scope = secret.Scope,
        TagsJson = JsonSerializer.Serialize(secret.Tags.Order(StringComparer.OrdinalIgnoreCase)),
        Status = secret.Status,
        CreatedAt = secret.CreatedAt,
        UpdatedAt = secret.UpdatedAt,
        Versions = secret.Versions.Select(version => SecretVersionEntity.FromVersion(secret.Id, version)).ToList()
    };

    public Secret ToSecret() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Name = Name,
        DisplayName = DisplayName,
        Description = Description,
        TypeName = TypeName,
        StoreName = StoreName,
        Scope = Scope,
        Tags = new HashSet<string>(
            JsonSerializer.Deserialize<string[]>(TagsJson) ?? [],
            StringComparer.OrdinalIgnoreCase),
        Status = Status,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Versions = Versions.OrderBy(version => version.Version).Select(version => version.ToVersion()).ToList()
    };
}

internal sealed class SecretVersionEntity
{
    public string SecretId { get; set; } = "";
    public int Version { get; set; }
    public SecretStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Value { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public SecretEntity Secret { get; set; } = null!;

    public static SecretVersionEntity FromVersion(string secretId, SecretVersion version) => new()
    {
        SecretId = secretId,
        Version = version.Version,
        Status = version.Status,
        CreatedAt = version.CreatedAt,
        ExpiresAt = version.ExpiresAt,
        Value = version.Payload.Value,
        MetadataJson = JsonSerializer.Serialize(version.Payload.Metadata)
    };

    public SecretVersion ToVersion() => new()
    {
        Version = Version,
        Status = Status,
        CreatedAt = CreatedAt,
        ExpiresAt = ExpiresAt,
        Payload = new SecretPayload
        {
            Value = Value,
            Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(MetadataJson)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        }
    };
}
