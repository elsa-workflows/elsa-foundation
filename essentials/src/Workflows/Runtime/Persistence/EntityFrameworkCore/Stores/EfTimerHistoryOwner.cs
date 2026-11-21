using System.Runtime.CompilerServices;
using System.Linq.Expressions;
using System.Collections.Concurrent;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Query;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// One bounded policy/closure receipt on the existing execution-ownership row, never a synthetic timer or journal.
internal static class EfTimerHistoryOwner
{
    internal sealed record Document(string Scope, string Run, RuntimeHistoryCapturePolicy Policy,
        DurableTimerNoRecordCompletion? Completion = null, bool LostFacts = false);

    private static readonly Type[] OwnerTypes = [typeof(DurableTimerEntity), typeof(ExecutionLivenessStateEntity)];
    private static readonly Type[] ReadTypes = OwnerTypes.Concat(RuntimeHistoryEfModule.Chain.ContentColumns.Select(column => column.Entity))
        .Append(typeof(WorkflowExecutionStateEntity)).Distinct().ToArray();
    private static readonly IMutableModel RequiredModel = CreateRequiredModel(false);
    private static readonly IMutableModel ReadModel = CreateRequiredModel(true);
    private static readonly ConcurrentDictionary<Type, ValueComparer> RequiredComparers = new();
    private static readonly ValueComparer SqlServerIdentityComparer = new CaseInsensitiveValueComparer();

    private static IMutableModel CreateRequiredModel(bool includeEvidence)
    {
        var model = new ModelBuilder();
        foreach (var type in includeEvidence ? ReadTypes : OwnerTypes)
        foreach (var member in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            model.Entity(type).Property(member.PropertyType, member.Name);
        model.ApplyConfiguration(new Configuration.DurableTimerEntityConfiguration());
        model.ApplyConfiguration(new Configuration.ExecutionLivenessStateEntityConfiguration());
        if (includeEvidence)
        {
            model.ApplyConfiguration(new Configuration.WorkflowExecutionStateEntityConfiguration());
            Configuration.RuntimeHistoryEntityConfiguration.Configure(model);
            Configuration.RuntimeHistoryOccurrenceEntityConfiguration.Configure(model);
        }
        return model.Model;
    }

    internal static bool HasReadableModel(RuntimeDbContext context) => HasReadableEntityModel(context, typeof(ExecutionLivenessStateEntity));
    internal static bool HasReadableTimerModel(RuntimeDbContext context) => HasReadableEntityModel(context, typeof(DurableTimerEntity));
    internal static bool HasReadableNativeModel(RuntimeDbContext context) => HasReadableEntityModel(context, typeof(WorkflowExecutionStateEntity));
    internal static bool HasWritableTimerModel(RuntimeDbContext context) => HasWritableEntityModel(context, typeof(DurableTimerEntity));

    private static bool HasReadableEntityModel(RuntimeDbContext context, Type entityType)
    {
        var required = ReadModel.FindEntityType(entityType)!;
        var actual = context.Model.FindEntityType(required.ClrType);
        var schema = required.GetSchema() ?? EfSchemaOptionsExtension.Find(context);
        if (actual is null || actual.GetDeclaredQueryFilters().Any() || actual.GetMappingFragments().Any() || actual.GetTableName() != required.GetTableName() ||
            actual.GetSchema() != schema || actual.GetViewName() is not null || actual.GetSqlQuery() is not null || actual.GetFunctionName() is not null ||
            actual.BaseType is not null || actual.GetDerivedTypes().Any() || actual.FindDiscriminatorProperty() is not null ||
            actual.GetNavigations().Any() || actual.GetSkipNavigations().Any() || actual.GetForeignKeys().Any() ||
            actual.GetProperties().Count() != required.GetProperties().Count() ||
            actual.FindPrimaryKey() is not { } key ||
            !key.Properties.Select(property => property.Name).SequenceEqual(required.FindPrimaryKey()!.Properties.Select(property => property.Name))) return false;
        var actualTable = StoreObjectIdentifier.Table(actual.GetTableName()!, actual.GetSchema());
        var expectedTable = StoreObjectIdentifier.Table(required.GetTableName()!, required.GetSchema());
        foreach (var expected in required.GetProperties())
        {
            var property = actual.FindProperty(expected.Name);
            if (property?.GetColumnName(actualTable) != expected.GetColumnName(expectedTable) || property?.PropertyInfo is not { } member ||
                member.DeclaringType != expected.PropertyInfo?.DeclaringType || member.Name != expected.Name || property.ClrType != expected.ClrType) return false;
            var ownField = expected.PropertyInfo!.DeclaringType!.GetField("<" + expected.Name + ">k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (property.FieldInfo is { } field && !Equals(field, ownField)) return false;
            var effective = property.GetTypeMapping().Converter;
            // Only declared payload columns may use the native framing converter. Every read predicate and
            // evidence projection must also retain the provider's ordinary effective scalar representation.
            var nativePayload = (expected.Name is "ContentJson" or "PayloadJson" or "MetadataJson" or "CleanupSafeFailureJson" or "TimerHistoryOwnerJson") &&
                property.GetValueConverter() is EfPayloadValueConverter && effective is EfPayloadValueConverter;
            if (!nativePayload && (property.GetValueConverter()?.GetType() != expected.GetValueConverter()?.GetType() ||
                property.GetProviderClrType() != expected.GetProviderClrType() ||
                effective?.GetType() != expected.GetValueConverter()?.GetType())) return false;
        }
        return true;
    }

    internal static bool HasModel(RuntimeDbContext context) =>
        ReadTypes.All(type => HasReadableEntityModel(context, type)) && OwnerTypes.All(type => HasWritableEntityModel(context, type));

    private static bool HasWritableEntityModel(RuntimeDbContext context, Type entityType)
    {
        if (!HasWriteTracking(context)) return false;
        if (!HasReadableEntityModel(context, entityType)) return false;
        var required = RequiredModel.FindEntityType(entityType)!;
        var actual = context.Model.FindEntityType(entityType)!;
        foreach (var expected in required.GetProperties())
        {
            var property = actual.FindProperty(expected.Name);
            if (property is null || property.PropertyInfo is not { } binding || binding.DeclaringType != required.ClrType ||
                binding.Name != expected.Name || property.ClrType != expected.ClrType || property.IsNullable != expected.IsNullable ||
                property.IsConcurrencyToken != expected.IsConcurrencyToken || property.ValueGenerated != expected.ValueGenerated ||
                property.GetBeforeSaveBehavior() != expected.GetBeforeSaveBehavior() || property.GetAfterSaveBehavior() != expected.GetAfterSaveBehavior() ||
                property.GetMaxLength() != expected.GetMaxLength() || !HasCanonicalComparers(context, property, expected.ClrType,
                    required.FindPrimaryKey()!.Properties.Any(key => key.Name == expected.Name))) return false;
        }
        foreach (var index in required.GetIndexes())
        {
            var names = index.Properties.Select(property => property.Name).ToArray();
            if (!actual.GetIndexes().Any(candidate => (!index.IsUnique || candidate.IsUnique && candidate.Properties.Count == names.Length) &&
                candidate.Properties.Take(names.Length).Select(property => property.Name).SequenceEqual(names))) return false;
        }
        return true;
    }

    internal static bool HasWriteTracking(RuntimeDbContext context) => context.ChangeTracker.AutoDetectChangesEnabled &&
        context.ChangeTracker.QueryTrackingBehavior == QueryTrackingBehavior.TrackAll;

    internal static void EnsureWriteTracking(RuntimeDbContext context)
    {
        if (!HasWriteTracking(context)) throw new NotSupportedException("Timer history write tracking changed during the owned operation.");
    }

    private static bool HasCanonicalComparers(RuntimeDbContext context, IProperty property, Type type, bool primaryKey)
    {
        // Explicit property comparers and replaced type mappings can both suppress tracked writes.
        // Anchor comparison/snapshot semantics to the owned scalar type, not a replaceable service.
        // Runtime models consume comparer annotations into their effective property comparer.
        var required = primaryKey && type == typeof(string) && context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
            ? SqlServerIdentityComparer : RequiredComparers.GetOrAdd(type, CreateRequiredComparer);
        return HasTrustedComparer(property.GetValueComparer(), required) &&
            (!primaryKey || HasTrustedComparer(property.GetKeyValueComparer(), required));
    }

    private static bool HasTrustedComparer(ValueComparer? actual, ValueComparer required)
    {
        // Expressions alone cannot qualify an application subclass overriding EF's virtual dispatch.
        if (actual is null || actual.GetType().Assembly != typeof(ValueComparer).Assembly) return false;
        var expressions = ExpressionEqualityComparer.Instance;
        return expressions.Equals(actual.EqualsExpression, required.EqualsExpression) &&
            expressions.Equals(actual.HashCodeExpression, required.HashCodeExpression) &&
            expressions.Equals(actual.SnapshotExpression, required.SnapshotExpression);
    }

    private static ValueComparer CreateRequiredComparer(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is not { } scalar) return ValueComparer.CreateDefault(type, false);
        var inner = ValueComparer.CreateDefault(scalar, false);
        // EF's structural expression comparison includes parameter names.
        var left = Expression.Parameter(type, "v1");
        var right = Expression.Parameter(type, "v2");
        var leftPresent = Expression.Property(left, nameof(Nullable<int>.HasValue));
        var rightPresent = Expression.Property(right, nameof(Nullable<int>.HasValue));
        var leftValue = Expression.Convert(left, scalar);
        // Construct the ordinary nullable scalar comparison through public expression APIs;
        // no provider service or EF internal comparer implementation supplies this authority.
        var equals = Expression.Lambda(Expression.OrElse(
            Expression.AndAlso(leftPresent, Expression.AndAlso(rightPresent,
                inner.ExtractEqualsBody(leftValue, Expression.Convert(right, scalar)))),
            Expression.AndAlso(Expression.Not(leftPresent), Expression.Not(rightPresent))), left, right);
        var value = Expression.Parameter(type, "v");
        var present = Expression.Property(value, nameof(Nullable<int>.HasValue));
        var scalarValue = Expression.Convert(value, scalar);
        var hash = Expression.Lambda(Expression.Condition(present,
            inner.ExtractHashCodeBody(scalarValue), Expression.Constant(0)), value);
        var snapshot = Expression.Lambda(Expression.Condition(present,
            Expression.Convert(inner.ExtractSnapshotBody(scalarValue), type), Expression.Default(type)), value);
        return (ValueComparer)Activator.CreateInstance(typeof(ValueComparer<>).MakeGenericType(type), equals, hash, snapshot)!;
    }

    internal static async ValueTask<ExecutionLivenessStateEntity> CoordinateFirstNativeAsync(RuntimeDbContext context,
        string scope, string run, CancellationToken token)
    {
        if (!HasReadableModel(context)) throw new NotSupportedException("First native state requires readable original timer ownership.");
        var id = Id(scope, run);
        // Change the revision: MySQL's supported changed-row count mode reports zero for a no-op update.
        var locked = await context.ExecutionLivenessStates.Where(row => row.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1), token);
        if (locked == 0)
        {
            var empty = EfExecutionLivenessStateStore.ToEntity(context,
                new(RuntimeExecutionOwnershipStateId.For(run), run, null, null, null, null,
                    metadata: new Dictionary<string, string> { [Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OwnershipFencingToken] = "0" }), scope, 1);
            // Insert only this address inside the caller's transaction. Saving this context here would
            // flush staged source-authority/native participants before their checkpoint is validated.
            var entity = context.Model.FindEntityType(typeof(ExecutionLivenessStateEntity))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = context.GetService<ISqlGenerationHelper>();
            var properties = RequiredModel.FindEntityType(typeof(ExecutionLivenessStateEntity))!.GetProperties().ToArray();
            var columns = properties.Select(property => sql.DelimitIdentifier(entity.FindProperty(property.Name)!.GetColumnName(table)!));
            var values = properties.Select(property => property.PropertyInfo!.GetValue(empty)).ToArray();
            var parameters = Enumerable.Range(0, values.Length).Select(index => "{" + index + "}");
            var command = "INSERT INTO " + sql.DelimitIdentifier(table.Name, table.Schema) +
                " (" + string.Join(", ", columns) + ") VALUES (" + string.Join(", ", parameters) + ")";
            await context.Database.ExecuteSqlInterpolatedAsync(FormattableStringFactory.Create(command, values), token);
        }
        var observed = await ReadAsync(context, scope, run, false, token);
        if (observed is null || await context.ExecutionLivenessStates.Where(row => row.Id == id && row.Revision == observed.Revision)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1), token) != 1)
            throw new NotSupportedException("A stale caller snapshot cannot establish current first-native ownership.");
        return observed;
    }

    internal static string Id(string scope, string run) => CompositeId(scope, run, RuntimeExecutionOwnershipStateId.For(run));

    internal static async ValueTask<ExecutionLivenessStateEntity?> ReadAsync(RuntimeDbContext context, string scope,
        string run, bool tracking, CancellationToken token)
    {
        if (!HasReadableModel(context)) throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
            "The native model cannot inspect original timer responsibility.");
        var id = Id(scope, run);
        var query = tracking ? context.ExecutionLivenessStates.AsTracking() : context.ExecutionLivenessStates.AsNoTracking();
        var row = await query.SingleOrDefaultAsync(item => item.Id == id, token);
        if (row is not null) _ = EfExecutionLivenessStateStore.Read(row, scope, run, RuntimeExecutionOwnershipStateId.For(run));
        return row;
    }

    internal static Document? Read(ExecutionLivenessStateEntity? row, string scope, string run)
    {
        if (row is null) return null;
        try
        {
            var content = RuntimeOperationalStateEfModule.Chain.Upcast<ExecutionLivenessStateEntity>(row.SchemaVersion,
                (nameof(row.ContentJson), row.ContentJson), (nameof(row.TimerHistoryOwnerJson), row.TimerHistoryOwnerJson));
            if (content[nameof(row.TimerHistoryOwnerJson)] is not { } json) return null;
            if (json.Length > 16 * 1024) throw new InvalidDataException("The timer-owner receipt exceeds its bound.");
            var value = RuntimeArtifactJson.Deserialize<Document>(json);
            if (value is null || value.Scope != scope || value.Run != run || value.Policy is null ||
                !DurableTimerHistoryFacts.Kinds.All(kind => value.Policy.SupportedKinds.Contains(kind, StringComparer.Ordinal)) ||
                value.LostFacts && value.Completion is null)
                throw new InvalidDataException("The timer-owner receipt disagrees with its original scope and policy.");
            return value;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException or InvalidDataException)
        { throw new RuntimeHistoryIntegrityException(run, "the timer source ownership receipt is invalid", error); }
    }

    internal static void Stage(RuntimeDbContext context, ExecutionLivenessStateEntity row, Document value)
    {
        EnsureWriteTracking(context);
        // Preconstruct every bounded input before any source mutation.
        var json = RuntimeArtifactJson.Serialize(value);
        if (json.Length > 16 * 1024) throw new ArgumentException("The timer-owner receipt exceeds its bound.", nameof(value));
        row.SchemaVersion = RuntimeSchemaWriteVersion.TimerHistory(context, row.SchemaVersion);
        row.TimerHistoryOwnerJson = json;
        var entry = context.Entry(row);
        entry.Property(item => item.SchemaVersion).IsModified = true;
        entry.Property(item => item.TimerHistoryOwnerJson).IsModified = true;
    }

    internal static async ValueTask LockAsync(RuntimeDbContext context, string scope, string run, CancellationToken token)
    {
        // A current write precedes the first consistent SELECT, including InnoDB repeatable-read waiters.
        // Advancing the native revision also invalidates an older fence cursor/snapshot at its real CAS.
        var id = Id(scope, run);
        await context.ExecutionLivenessStates.Where(row => row.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1), token);
        // An unchanged native ownership row may already be tracked by the caller. Refresh that
        // actual row after the lock so identity resolution cannot restore its pre-lock revision.
        var tracked = context.ChangeTracker.Entries<ExecutionLivenessStateEntity>().SingleOrDefault(entry => entry.Entity.Id == id);
        if (tracked is not null) await tracked.ReloadAsync(token);
    }

    internal static void EnsurePolicy(Document? owner, string scope, string run, RuntimeHistoryCapturePolicy? policy)
    {
        if (owner is not null && (owner.Scope != scope || owner.Run != run || !owner.Policy.IsEquivalentTo(policy)))
            throw new RuntimeHistoryIntegrityException(run, "the native capture choice disagrees with its original timer owner");
    }

    internal static void EnsureNativeScope(PersistenceAccessContext access, WorkflowExecutionState native)
    {
        var scope = access.RequireScope().Value;
        if (!native.HasBoundIngressScope(access.RequireScope())) access.EnsureTenantScope(native.TenantId);
        if (native.Partition is { } partition && partition.Value != scope)
            throw new InvalidOperationException("The timer's native execution belongs to another physical partition.");
    }
}
