using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Direct contract tests for the unload-safe OpenAPI convention. These tests deliberately use
/// collectible Reflection.Emit types so each unsafe metadata branch is exercised without relying on
/// a process-wide framework cache or a private cache-clearing workaround.
/// </summary>
public sealed class OpenApiLifetimeBoundaryTests
{
    [Fact]
    public void Accepted_metadata_gets_one_immutable_value_only_marker()
    {
        var builder = Endpoint("GET /safe", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));
        builder.Metadata.Add(new AcceptsMetadata(typeof(StableRequest)));
        builder.Metadata.Add(new ProducesMetadata(typeof(StableResponse), StatusCodes.Status200OK));

        OpenApiLifetimeValidator.ValidateAndMark(builder);

        var marker = Assert.Single(builder.Metadata.OfType<OpenApiLifetimeMetadata>());
        Assert.Equal("Elsa.Tests", marker.Owner);
        Assert.Equal(OpenApiLifetimeClassification.SharedContract, marker.Classification);
        Assert.Equal("GET /safe", marker.Endpoint);
        Assert.Equal(OpenApiLifetimeValidationCategories.All, marker.CheckedCategories);
        Assert.Single(builder.Metadata.OfType<OpenApiLifetimeMetadata>());
        Assert.All(typeof(OpenApiLifetimeMetadata).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void Serializer_metadata_objects_are_rejected_even_for_a_stable_contract_type()
    {
        var builder = Endpoint("POST /stable-json", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(JsonTypeInfo.CreateJsonTypeInfo(typeof(StableRequest), new JsonSerializerOptions()));

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.SerializerMetadata &&
            violation.ArtifactIdentity.Contains("JsonTypeInfo objects are not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void Marker_and_validator_have_null_guards_and_reject_invalid_marker_values()
    {
        Assert.Throws<ArgumentNullException>(() => OpenApiLifetimeValidator.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => OpenApiLifetimeValidator.ValidateAndMark(null!));
        Assert.Throws<ArgumentNullException>(() => new OpenApiLifetimeMetadata(null!, OpenApiLifetimeClassification.HostStatic, "GET /safe"));
        Assert.Throws<ArgumentException>(() => new OpenApiLifetimeMetadata("owner", OpenApiLifetimeClassification.HostStatic, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenApiLifetimeMetadata("owner", (OpenApiLifetimeClassification)99, "GET /safe"));
        Assert.Throws<ArgumentException>(() => new OpenApiLifetimeMetadata("owner", OpenApiLifetimeClassification.HostStatic, "GET /safe", default));
    }

    [Fact]
    public void Dynamic_api_explorer_refresh_registration_is_idempotent_and_null_guarded()
    {
        Assert.Throws<ArgumentNullException>(() => new EndpointDataSourceActionDescriptorChangeProvider(null!));
        Assert.Throws<ArgumentNullException>(() => OpenApiLifetimeServiceCollectionExtensions.AddDynamicEndpointApiExplorerRefresh(null!));

        var source = new TestEndpointDataSource();
        var services = new ServiceCollection();
        services.AddSingleton<EndpointDataSource>(source);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddDynamicEndpointApiExplorerRefresh();
        using var provider = services.BuildServiceProvider();

        var changeProvider = Assert.Single(provider.GetServices<IActionDescriptorChangeProvider>());
        Assert.IsType<EndpointDataSourceActionDescriptorChangeProvider>(changeProvider);
        Assert.Same(source.GetChangeToken(), changeProvider.GetChangeToken());
    }

    [Fact]
    public void RequireStableOpenApi_returns_the_same_builder_and_registers_a_final_convention()
    {
        var conventions = new RecordingConventionBuilder();
        var result = conventions.RequireStableOpenApi();

        Assert.Same(conventions, result);
        Assert.Empty(conventions.OrdinaryConventions);
        var builder = Endpoint("GET /safe", EndpointOwnershipMetadata.Host("Elsa.Tests"));
        conventions.FinalConventions.Single()(builder);
        Assert.Single(builder.Metadata.OfType<OpenApiLifetimeMetadata>());
    }

    [Fact]
    public void Final_convention_runs_after_metadata_added_by_ordinary_conventions()
    {
        var conventions = new RecordingConventionBuilder();
        conventions.Add(builder => builder.Metadata.Add(new AcceptsMetadata(CreateCollectibleType("RequestAfterConvention"))));
        conventions.RequireStableOpenApi();
        var builder = Endpoint("GET /late", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));

        conventions.OrdinaryConventions.Single()(builder);
        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => conventions.FinalConventions.Single()(builder));

        Assert.Equal(OpenApiLifetimeViolationCategory.RequestType, exception.Violation.Category);
        Assert.Contains("owner='Elsa.Tests'; shell='shell-a'; generation=7; endpoint='GET /late'; category=RequestType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_and_duplicate_ownership_fail_before_artifact_inspection()
    {
        var missing = Endpoint("GET /missing");
        var missingException = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(missing));
        Assert.Equal(OpenApiLifetimeViolationCategory.MissingOwnership, missingException.Violation.Category);

        var duplicate = Endpoint("GET /duplicate", EndpointOwnershipMetadata.Module("Elsa.One"));
        duplicate.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.Two"));
        var duplicateException = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(duplicate));
        Assert.Equal(OpenApiLifetimeViolationCategory.DuplicateOwnership, duplicateException.Violation.Category);
        Assert.Contains("Elsa.One, Elsa.Two", duplicateException.Message, StringComparison.Ordinal);

        var conflictingDynamic = Endpoint(
            "GET /duplicate-dynamic",
            EndpointOwnershipMetadata.DynamicShell("Elsa.One", "shell-a", 1));
        conflictingDynamic.Metadata.Add(EndpointOwnershipMetadata.DynamicShell("Elsa.Two", "shell-b", 2));
        var conflictingException = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(conflictingDynamic));
        Assert.Equal(OpenApiLifetimeViolationCategory.DuplicateOwnership, conflictingException.Violation.Category);
        Assert.Null(conflictingException.Violation.Shell);
        Assert.Null(conflictingException.Violation.Generation);
    }

    [Theory]
    [MemberData(nameof(UnsafeMetadataCases))]
    public void Every_collectible_api_explorer_artifact_is_rejected(
        OpenApiLifetimeViolationCategory expectedCategory,
        Func<object> metadataFactory)
    {
        var builder = Endpoint("GET /unsafe", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));
        builder.Metadata.Add(metadataFactory());

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == expectedCategory &&
            violation.LoadContextIdentity.Contains("collectible", StringComparison.Ordinal));
        Assert.All(exception.Violations, violation =>
        {
            Assert.Equal("Elsa.Tests", violation.Owner);
            Assert.Equal("shell-a", violation.Shell);
            Assert.Equal(7, violation.Generation);
            Assert.Equal("GET /unsafe", violation.Endpoint);
            Assert.DoesNotContain("0x", violation.ArtifactIdentity, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Unknown_metadata_getter_fails_closed_without_exposing_the_object()
    {
        var builder = Endpoint("GET /unknown", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new ThrowingMetadata());

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        var violation = Assert.Single(exception.Violations);
        Assert.Equal(OpenApiLifetimeViolationCategory.UnknownMetadataShape, violation.Category);
        Assert.Contains("getter failed", violation.ArtifactIdentity, StringComparison.Ordinal);
        Assert.DoesNotContain("test-only getter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_collectible_metadata_graph_is_rejected()
    {
        var builder = Endpoint("GET /nested", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new NestedMetadata(new NestedMetadataValue(CreateCollectibleType("NestedContract"))));

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.MetadataObject &&
            violation.ArtifactIdentity.Contains("NestedContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_collectible_metadata_graph_is_rejected()
    {
        var builder = Endpoint("GET /private", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new PrivateMetadata(Activator.CreateInstance(CreateCollectibleType("PrivateContract"))!));

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.MetadataObject &&
            violation.ArtifactIdentity.Contains("PrivateContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Stable_generic_metadata_with_collectible_type_argument_is_rejected()
    {
        var builder = Endpoint("GET /generic", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        var metadataType = typeof(GenericMetadata<>).MakeGenericType(CreateCollectibleType("GenericContract"));
        builder.Metadata.Add(Activator.CreateInstance(metadataType)!);

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.MetadataObject &&
            violation.ArtifactIdentity.Contains("GenericContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Stable_delegate_target_with_private_collectible_state_is_rejected()
    {
        var builder = Endpoint("GET /captured", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        var target = new StableCallbackTarget(Activator.CreateInstance(CreateCollectibleType("CapturedContract"))!);
        builder.Metadata.Add((Action)target.Invoke);

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.DelegateOrTransformer &&
            violation.ArtifactIdentity.Contains("CapturedContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_multicast_delegate_invocation_is_inspected()
    {
        var builder = Endpoint("GET /multicast", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        var collectible = (Action)CreateCollectibleDelegate();
        Action stable = StableCallback;
        builder.Metadata.Add(collectible + stable);

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.DelegateOrTransformer &&
            violation.ArtifactIdentity.Contains("CollectibleMethod", StringComparison.Ordinal));
    }

    [Fact]
    public void Unbounded_metadata_enumeration_fails_closed()
    {
        var builder = Endpoint("GET /enumeration", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(Enumerable.Repeat<object>("value", 257));

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.UnknownMetadataShape &&
            violation.ArtifactIdentity.Contains("enumeration exceeds 256 items", StringComparison.Ordinal));
    }

    [Fact]
    public void Enumerable_private_collectible_state_is_rejected_after_yielded_values_are_inspected()
    {
        var builder = Endpoint("GET /enumerable-state", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new StableEnumerableMetadata(Activator.CreateInstance(CreateCollectibleType("EnumerableContract"))!));

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.MetadataObject &&
            violation.ArtifactIdentity.Contains("EnumerableContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Stable_generic_method_metadata_with_collectible_signature_is_rejected()
    {
        var builder = Endpoint("POST /method-signature", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        var method = typeof(OpenApiLifetimeBoundaryTests)
            .GetMethod(nameof(StableGenericMethod), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(CreateCollectibleType("MethodContract"));
        builder.Metadata.Add(method);

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.MemberOrMethod &&
            violation.ArtifactIdentity.Contains("MethodContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Stable_declared_delegate_with_collectible_signature_is_rejected()
    {
        var contractType = CreateCollectibleType("DelegateContract");
        var method = typeof(OpenApiLifetimeBoundaryTests)
            .GetMethod(nameof(StableGenericMethod), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(contractType);
        var delegateType = typeof(Func<,>).MakeGenericType(contractType, contractType);
        var callback = method.CreateDelegate(delegateType);
        var builder = Endpoint("POST /delegate-signature", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(callback);

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == OpenApiLifetimeViolationCategory.DelegateOrTransformer &&
            violation.ArtifactIdentity.Contains("DelegateContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Conflicting_existing_lifetime_marker_fails_closed()
    {
        var builder = Endpoint("GET /marker", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new OpenApiLifetimeMetadata(
            "Elsa.Other",
            OpenApiLifetimeClassification.SharedContract,
            "GET /other"));

        var exception = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.ValidateAndMark(builder));

        Assert.Equal(OpenApiLifetimeViolationCategory.UnknownMetadataShape, exception.Violation.Category);
        Assert.Contains("lifetime marker", exception.Violation.ArtifactIdentity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostics_are_owner_aware_and_stable_across_repeated_validation()
    {
        var collectibleType = CreateCollectibleType("StableDiagnosticRequest");
        var first = Endpoint("GET /diagnostic", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-b", 11));
        first.Metadata.Add(new ProducesMetadata(collectibleType, StatusCodes.Status200OK));
        first.Metadata.Add(new AcceptsMetadata(collectibleType));
        var second = Endpoint("GET /diagnostic", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-b", 11));
        second.Metadata.Add(new ProducesMetadata(collectibleType, StatusCodes.Status200OK));
        second.Metadata.Add(new AcceptsMetadata(collectibleType));

        var firstException = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(first));
        var secondException = Assert.Throws<UnsafeOpenApiMetadataException>(() => OpenApiLifetimeValidator.Validate(second));

        Assert.Equal(firstException.Message, secondException.Message);
        Assert.Equal(
            firstException.Violations.Select(violation => violation.Category),
            firstException.Violations.Select(violation => violation.Category).OrderBy(category => category));
        Assert.Contains("owner='Elsa.Tests'; shell='shell-b'; generation=11; endpoint='GET /diagnostic'", firstException.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> UnsafeMetadataCases()
    {
        yield return [OpenApiLifetimeViolationCategory.RequestType, () => new AcceptsMetadata(CreateCollectibleType("RequestContract"))];
        yield return [OpenApiLifetimeViolationCategory.ResponseType, () => new ProducesMetadata(CreateCollectibleType("ResponseContract"), StatusCodes.Status200OK)];
        yield return [OpenApiLifetimeViolationCategory.MetadataObject, () => Activator.CreateInstance(CreateCollectibleType("MetadataObject"))!];
        yield return [OpenApiLifetimeViolationCategory.MemberOrMethod, () => CreateCollectibleMethod()];
        yield return [OpenApiLifetimeViolationCategory.DelegateOrTransformer, () => CreateCollectibleDelegate()];
        yield return [OpenApiLifetimeViolationCategory.SerializerMetadata, () => JsonTypeInfo.CreateJsonTypeInfo(CreateCollectibleType("SerializerContract"), new JsonSerializerOptions())];
    }

    private static TestEndpointBuilder Endpoint(string displayName, params EndpointOwnershipMetadata[] ownership)
    {
        var builder = new TestEndpointBuilder { DisplayName = displayName };
        foreach (var metadata in ownership)
            builder.Metadata.Add(metadata);
        return builder;
    }

    private static Type CreateCollectibleType(string name)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Elsa.OpenApiBoundary.{name}.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule(name + ".Module");
        return module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class).CreateType()!;
    }

    private static MethodInfo CreateCollectibleMethod()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"Elsa.OpenApiBoundary.Method.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Method.Module");
        var type = module.DefineType("CollectibleMethod", TypeAttributes.Public | TypeAttributes.Class);
        var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod("Execute")!;
    }

    private static Delegate CreateCollectibleDelegate() =>
        Delegate.CreateDelegate(typeof(Action), CreateCollectibleMethod());

    private sealed class StableRequest;
    private sealed class StableResponse;

    private sealed class AcceptsMetadata(Type? requestType) : IAcceptsMetadata
    {
        public IReadOnlyList<string> ContentTypes { get; } = ["application/json"];
        public Type? RequestType { get; } = requestType;
        public bool IsOptional => false;
    }

    private sealed class ProducesMetadata(Type? type, int statusCode) : IProducesResponseTypeMetadata
    {
        public Type? Type { get; } = type;
        public int StatusCode { get; } = statusCode;
        public string? Description => null;
        public IEnumerable<string> ContentTypes { get; } = ["application/json"];
    }

    private sealed class ThrowingMetadata
    {
        public Type Contract => throw new InvalidOperationException("test-only getter");
    }

    private sealed record NestedMetadata(NestedMetadataValue Value);

    private sealed record NestedMetadataValue(Type Contract);

    private sealed class PrivateMetadata(object value)
    {
        private readonly object _value = value;
    }

    private sealed class GenericMetadata<T>;

    private sealed class StableCallbackTarget(object value)
    {
        private readonly object _value = value;

        public void Invoke()
        {
            GC.KeepAlive(_value);
        }
    }

    private sealed class StableEnumerableMetadata(object value) : IEnumerable<object>
    {
        private readonly object _value = value;

        public IEnumerator<object> GetEnumerator()
        {
            yield return "stable";
            GC.KeepAlive(_value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void StableCallback()
    {
    }

    private static T StableGenericMethod<T>(T value) => value;

    private sealed class TestEndpointBuilder : EndpointBuilder
    {
        public override Endpoint Build() =>
            new(_ => Task.CompletedTask, new EndpointMetadataCollection(Metadata), DisplayName);
    }

    private sealed class TestEndpointDataSource : EndpointDataSource
    {
        private readonly IChangeToken _changeToken = new CancellationChangeToken(CancellationToken.None);

        public override IReadOnlyList<Endpoint> Endpoints => [];

        public override IChangeToken GetChangeToken() => _changeToken;
    }

    private sealed class RecordingConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> OrdinaryConventions { get; } = [];
        public List<Action<EndpointBuilder>> FinalConventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => OrdinaryConventions.Add(convention);
        public void Finally(Action<EndpointBuilder> finallyConvention) => FinalConventions.Add(finallyConvention);
    }
}
