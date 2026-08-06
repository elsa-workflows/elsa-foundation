using System.Reflection;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class PreparedRecoveryRouterTests
{
    [Fact]
    public void Recovery_status_is_the_closed_six_outcome_protocol()
    {
        var statusType = RecoveryAuthorityTestContract.RequiredType(
            "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryStatus");

        Assert.True(statusType.IsEnum);
        Assert.Equal(
            ["Absent", "Exact", "Missing", "FingerprintMismatch", "UnsupportedVersionOrKind", "Ambiguous"],
            Enum.GetNames(statusType));
        var routeType = RecoveryAuthorityTestContract.RequiredType(
            "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryRoute");
        Assert.True(routeType.IsEnum);
        Assert.Equal(["SourceFree", "SourceBound"], Enum.GetNames(routeType));
    }

    [Fact]
    public async Task Null_original_authority_routes_Absent_without_consulting_durable_scheduler_work()
    {
        var fixture = NewRouterFixture();

        var resolution = await InvokeRouterAsync(fixture.Router, authority: null);

        Assert.Equal("Absent", StatusName(resolution));
        Assert.Equal(0, fixture.Resolver.CallCount);
        Assert.Null(OptionalProperty(resolution, "WorkItem"));
    }

    [Theory]
    [InlineData("Exact", true)]
    [InlineData("Missing", false)]
    [InlineData("FingerprintMismatch", false)]
    [InlineData("UnsupportedVersionOrKind", false)]
    [InlineData("Ambiguous", false)]
    public async Task Declared_authority_preserves_each_non_absent_resolver_outcome(string status, bool carriesWorkItem)
    {
        var fixture = NewRouterFixture();
        var item = AuthorityWorkItem.Sample().ToRuntimeWorkItem();
        var authority = RecoveryAuthorityTestContract.Encode(item);
        fixture.Resolver.Result = NewResolution(status, authority, carriesWorkItem ? item : null);

        var resolution = await InvokeRouterAsync(fixture.Router, authority);

        Assert.Equal(status, StatusName(resolution));
        Assert.Equal(1, fixture.Resolver.CallCount);
        Assert.Equal(carriesWorkItem ? item : null, OptionalProperty(resolution, "WorkItem"));
        Assert.NotEqual("Absent", StatusName(resolution));
    }

    [Fact]
    public async Task Declared_authority_can_never_be_downgraded_to_Absent_by_a_provider_result()
    {
        var fixture = NewRouterFixture();
        var item = AuthorityWorkItem.Sample().ToRuntimeWorkItem();
        var authority = RecoveryAuthorityTestContract.Encode(item);
        fixture.Resolver.Result = NewResolution("Absent", authority: null, workItem: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeRouterAsync(fixture.Router, authority));
        Assert.Equal(1, fixture.Resolver.CallCount);
    }

    [Fact]
    public void Resolver_contract_is_bounded_to_250_and_returns_the_closed_resolution_model()
    {
        var resolverType = ResolverType;
        Assert.True(resolverType.IsInterface);
        Assert.Equal(250, RecoveryAuthorityTestContract.RequiredConstant<int>(resolverType, "MaximumPageSize"));
        var resolutionType = ResolutionType;
        Assert.Equal(
            RecoveryAuthorityTestContract.RequiredType("Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryRoute"),
            Nullable.GetUnderlyingType(resolutionType.GetProperty("Route")?.PropertyType ?? typeof(void)) ??
            resolutionType.GetProperty("Route")?.PropertyType);
        var method = Assert.Single(resolverType.GetMethods());
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == RecoveryAuthorityTestContract.AuthorityType);
        Assert.Equal(typeof(ValueTask<>).MakeGenericType(resolutionType), method.ReturnType);
    }

    [Theory]
    [InlineData("Exact")]
    [InlineData("Missing")]
    [InlineData("FingerprintMismatch")]
    [InlineData("UnsupportedVersionOrKind")]
    [InlineData("Ambiguous")]
    public async Task Shipped_in_memory_resolver_behaviorally_classifies_every_declared_authority_outcome(string expected)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        Assert.True(ResolverType.IsInstanceOfType(queue),
            "The shipped durable in-memory queue must implement the recovery resolver directly.");
        var original = AuthorityWorkItem.Sample();
        object authority;

        switch (expected)
        {
            case "Exact":
                await queue.EnqueueAsync(original.ToRuntimeWorkItem());
                authority = RecoveryAuthorityTestContract.Encode(original.ToRuntimeWorkItem());
                break;
            case "Missing":
                authority = RecoveryAuthorityTestContract.Encode(original.ToRuntimeWorkItem());
                break;
            case "FingerprintMismatch":
                authority = RecoveryAuthorityTestContract.Encode(original.ToRuntimeWorkItem());
                await queue.EnqueueAsync(original.Change("payload").ToRuntimeWorkItem());
                break;
            case "UnsupportedVersionOrKind":
                authority = NewAuthority(version: 2, kind: "runtime.scheduler-work", original);
                await queue.EnqueueAsync(original.ToRuntimeWorkItem());
                break;
            case "Ambiguous":
                authority = RecoveryAuthorityTestContract.Encode(original.ToRuntimeWorkItem());
                await queue.EnqueueAsync(original.ToRuntimeWorkItem());
                InjectDuplicateIntoDurablePage(queue, original.ToRuntimeWorkItem());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expected), expected, null);
        }

        var resolution = await InvokeResolverAsync(queue, authority);

        Assert.Equal(expected, StatusName(resolution));
        var resolvedWorkItem = OptionalProperty(resolution, "WorkItem");
        if (expected != "Exact")
        {
            Assert.Null(resolvedWorkItem);
            return;
        }

        var exact = Assert.IsType<RuntimeSchedulerWorkItem>(resolvedWorkItem);
        Assert.Equal(original.WorkItemId, exact.WorkItemId);
        Assert.Equal(original.WorkflowExecutionId, exact.WorkflowExecutionId);
        Assert.Equal(original.CommandId, exact.CommandId);
        Assert.Equal(original.EnvelopeId, exact.EnvelopeId);
        Assert.Equal(original.IdempotencyKey, exact.IdempotencyKey);
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(authority, "Fingerprint"),
            RecoveryAuthorityTestContract.Property<string>(RecoveryAuthorityTestContract.Encode(exact), "Fingerprint"));
    }

    [Fact]
    public async Task Shipped_resolver_includes_an_unexpired_claimed_but_still_durable_item()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var item = AuthorityWorkItem.Sample().ToRuntimeWorkItem();
        await queue.EnqueueAsync(item);
        var claim = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            item.WorkflowExecutionId,
            "owner-1",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5)));
        Assert.NotNull(claim);

        var resolution = await InvokeResolverAsync(queue, RecoveryAuthorityTestContract.Encode(item));

        Assert.Equal("Exact", StatusName(resolution));
        var exact = Assert.IsType<RuntimeSchedulerWorkItem>(OptionalProperty(resolution, "WorkItem"));
        Assert.Equal(item.WorkItemId, exact.WorkItemId);
        Assert.Equal(item.WorkflowExecutionId, exact.WorkflowExecutionId);
        Assert.Equal(item.CommandId, exact.CommandId);
        Assert.Equal(item.EnvelopeId, exact.EnvelopeId);
        Assert.Equal(item.IdempotencyKey, exact.IdempotencyKey);
        Assert.Equal(
            RecoveryAuthorityTestContract.Property<string>(RecoveryAuthorityTestContract.Encode(item), "Fingerprint"),
            RecoveryAuthorityTestContract.Property<string>(RecoveryAuthorityTestContract.Encode(exact), "Fingerprint"));
    }

    [Fact]
    public async Task Unsupported_kind_is_rejected_before_any_durable_match_can_be_accepted()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var item = AuthorityWorkItem.Sample();
        await queue.EnqueueAsync(item.ToRuntimeWorkItem());

        var resolution = await InvokeResolverAsync(queue, NewAuthority(1, "other.kind", item));

        Assert.Equal("UnsupportedVersionOrKind", StatusName(resolution));
        Assert.Null(OptionalProperty(resolution, "WorkItem"));
    }

    private static Type ResolverType => RecoveryAuthorityTestContract.RequiredType(
        "Elsa.Workflows.Runtime.Core.Contracts.IRuntimeSchedulerWorkItemResolver");

    private static Type ResolutionType => RecoveryAuthorityTestContract.RequiredType(
        "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryResolution");

    private static Type StatusType => RecoveryAuthorityTestContract.RequiredType(
        "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryStatus");

    private static Type RouterType => typeof(RuntimeCheckpointCommitter).Assembly.GetType(
        "Elsa.Workflows.Runtime.Core.Services.RuntimeCheckpointRecoveryRouter",
        throwOnError: false) ?? throw new InvalidOperationException(
        "Required Path A router type 'Elsa.Workflows.Runtime.Core.Services.RuntimeCheckpointRecoveryRouter' is missing.");

    private static RouterFixture NewRouterFixture()
    {
        var resolver = DispatchProxy.Create(ResolverType, typeof(RecordingResolverProxy));
        var recording = Assert.IsAssignableFrom<RecordingResolverProxy>(resolver);
        var constructor = RouterType.GetConstructors().SingleOrDefault(candidate =>
            candidate.GetParameters().Any(parameter => parameter.ParameterType == ResolverType));
        Assert.NotNull(constructor);
        var arguments = constructor!.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == ResolverType)
                return resolver;
            if (parameter.ParameterType == RecoveryAuthorityTestContract.CodecType)
                return Activator.CreateInstance(RecoveryAuthorityTestContract.CodecType);
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;
            Assert.Fail($"Unexpected required recovery-router dependency '{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();
        return new RouterFixture(constructor.Invoke(arguments), recording);
    }

    private static object NewResolution(string status, object? authority, RuntimeSchedulerWorkItem? workItem)
    {
        var statusValue = Enum.Parse(StatusType, status);
        var constructor = ResolutionType.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).FirstOrDefault();
        Assert.NotNull(constructor);
        var arguments = constructor!.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == StatusType)
                return statusValue;
            if (parameter.ParameterType == RecoveryAuthorityTestContract.AuthorityType)
                return authority;
            if (parameter.ParameterType == typeof(RuntimeSchedulerWorkItem))
                return workItem;
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;
            Assert.Fail($"Unexpected required resolution field '{parameter.Name}:{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();
        return constructor.Invoke(arguments);
    }

    private static async Task<object> InvokeRouterAsync(object router, object? authority)
    {
        var method = RouterType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(candidate =>
                candidate.ReturnType == typeof(ValueTask<>).MakeGenericType(ResolutionType) &&
                candidate.GetParameters().Any(parameter => parameter.ParameterType == RecoveryAuthorityTestContract.AuthorityType));
        Assert.NotNull(method);
        var arguments = method!.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == RecoveryAuthorityTestContract.AuthorityType)
                return authority;
            if (parameter.ParameterType == typeof(CancellationToken))
                return CancellationToken.None;
            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;
            Assert.Fail($"Unexpected required router argument '{parameter.Name}:{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();
        object awaitable;
        try
        {
            awaitable = method.Invoke(router, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
        var task = (Task)awaitable.GetType().GetMethod("AsTask")!.Invoke(awaitable, null)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task<object> InvokeResolverAsync(object resolver, object authority)
    {
        var method = Assert.Single(ResolverType.GetMethods());
        var arguments = method.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == RecoveryAuthorityTestContract.AuthorityType)
                return authority;
            if (parameter.ParameterType == typeof(CancellationToken))
                return CancellationToken.None;
            Assert.Fail($"Unexpected required resolver argument '{parameter.Name}:{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();
        var awaitable = method.Invoke(resolver, arguments)!;
        var task = (Task)awaitable.GetType().GetMethod("AsTask")!.Invoke(awaitable, null)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static object NewAuthority(int version, string kind, AuthorityWorkItem item)
    {
        var valid = RecoveryAuthorityTestContract.Encode(item.ToRuntimeWorkItem());
        var constructor = Assert.Single(RecoveryAuthorityTestContract.AuthorityType.GetConstructors());
        var arguments = constructor.GetParameters().Select(parameter => parameter.Name?.ToLowerInvariant() switch
        {
            "version" => (object)version,
            "kind" => kind,
            "workflowexecutionid" => item.WorkflowExecutionId,
            "workitemid" => item.WorkItemId,
            "fingerprint" => RecoveryAuthorityTestContract.Property<string>(valid, "Fingerprint"),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected authority constructor parameter '{parameter.Name}'.")
        }).ToArray();
        return constructor.Invoke(arguments);
    }

    private static void InjectDuplicateIntoDurablePage(InMemoryWorkflowSchedulerWorkQueue queue, RuntimeSchedulerWorkItem item)
    {
        var field = typeof(InMemoryWorkflowSchedulerWorkQueue).GetField(
            "_queuesByWorkflowExecutionId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var dictionary = field!.GetValue(queue)!;
        var indexer = dictionary.GetType().GetProperty("Item");
        Assert.NotNull(indexer);
        var durableQueue = indexer!.GetValue(dictionary, [item.WorkflowExecutionId]);
        Assert.NotNull(durableQueue);
        durableQueue!.GetType().GetMethod("Enqueue")!.Invoke(durableQueue, [item]);
    }

    private static string StatusName(object resolution) =>
        OptionalProperty(resolution, "Status")?.ToString() ?? string.Empty;

    private static object? OptionalProperty(object value, string name) =>
        value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);

    private sealed record RouterFixture(object Router, RecordingResolverProxy Resolver);

    private class RecordingResolverProxy : DispatchProxy
    {
        public int CallCount { get; private set; }
        public object? Result { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            CallCount++;
            Assert.NotNull(Result);
            var valueTaskType = typeof(ValueTask<>).MakeGenericType(ResolutionType);
            return Activator.CreateInstance(valueTaskType, Result);
        }
    }
}
