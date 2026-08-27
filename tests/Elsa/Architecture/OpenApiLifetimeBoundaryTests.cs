using Elsa.Activities.Design.Api;
using Elsa.Api.AspNetCore;
using Elsa.Workflows.Publishing.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NativeEndpoints;
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
    public void Activities_design_native_openapi_metadata_uses_only_non_collectible_types()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddElsaEndpoints();
        using var app = builder.Build();

        ActivitiesDesignApi.MapActivitiesDesignApi(app);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId == "Elsa.Activities.Design.Api")
            .ToArray();

        Assert.Equal(38, endpoints.Length);
        Assert.All(endpoints, endpoint =>
        {
            var marker = Assert.Single(endpoint.Metadata.OfType<EndpointLifetimeMetadata>());
            Assert.Equal(EndpointLifetimeValidationCategories.All, marker.CheckedCategories);
            Assert.Equal("Elsa.Activities.Design.Api", marker.Group);

            var acceptedTypes = endpoint.Metadata
                .GetOrderedMetadata<IAcceptsMetadata>()
                .Select(metadata => metadata.RequestType)
                .Where(type => type is not null)
                .Cast<Type>();
            var responseTypes = endpoint.Metadata
                .GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Select(metadata => metadata.Type)
                .Where(type => type is not null)
                .Cast<Type>();

            Assert.All(acceptedTypes.Concat(responseTypes), type =>
                Assert.False(type.Assembly.IsCollectible, $"OpenAPI contract type '{type}' came from a collectible assembly."));
        });
    }

    [Fact]
    public void Publishing_native_openapi_metadata_uses_only_non_collectible_types()
    {
        var ownerAssembly = typeof(WorkflowsPublishingApiFeature).Assembly;
        var mapperType = ownerAssembly.GetType("Elsa.Workflows.Publishing.Api.WorkflowsPublishingApi", throwOnError: true)!;
        var mapper = mapperType.GetMethod("MapWorkflowsPublishingApi", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(mapper);

        var builder = WebApplication.CreateBuilder();

        builder.Services.AddElsaEndpoints();
        using var app = builder.Build();
        mapper!.Invoke(null, [app]);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId == "Elsa.Workflows.Publishing.Api")
            .ToArray();

        Assert.Equal(23, endpoints.Length);
        Assert.All(endpoints, endpoint =>
        {
            var lifetime = Assert.Single(endpoint.Metadata.OfType<EndpointLifetimeMetadata>());
            Assert.Equal(EndpointLifetimeValidationCategories.All, lifetime.CheckedCategories);
            Assert.Equal("Elsa.Workflows.Publishing.Api", lifetime.Group);

            var acceptedTypes = endpoint.Metadata
                .GetOrderedMetadata<IAcceptsMetadata>()
                .Select(metadata => metadata.RequestType)
                .Where(type => type is not null)
                .Cast<Type>();
            var responseTypes = endpoint.Metadata
                .GetOrderedMetadata<IProducesResponseTypeMetadata>()
                .Select(metadata => metadata.Type)
                .Where(type => type is not null)
                .Cast<Type>();

            Assert.All(acceptedTypes.Concat(responseTypes), type =>
            {
                if (type.Namespace?.StartsWith("Elsa.Workflows.Publishing.Api", StringComparison.Ordinal) == true)
                    Assert.Equal("Elsa.Workflows.Publishing.Api", type.Assembly.GetName().Name);
                Assert.False(type.Assembly.IsCollectible, $"OpenAPI contract type '{type}' came from a collectible assembly.");
            });
        });
    }

    [Fact]
    public void Accepted_metadata_gets_one_immutable_value_only_marker()
    {
        var builder = Endpoint("GET /safe", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));
        builder.Metadata.Add(new AcceptsMetadata(typeof(StableRequest)));
        builder.Metadata.Add(new ProducesMetadata(typeof(StableResponse), StatusCodes.Status200OK));

        EndpointLifetimeValidator.ValidateAndMark(builder);

        var marker = Assert.Single(builder.Metadata.OfType<EndpointLifetimeMetadata>());
        Assert.Equal("Elsa.Tests", marker.Group);
        Assert.Equal("GET /safe", marker.Endpoint);
        Assert.Equal(EndpointLifetimeValidationCategories.All, marker.CheckedCategories);
        Assert.Single(builder.Metadata.OfType<EndpointLifetimeMetadata>());
        Assert.All(typeof(EndpointLifetimeMetadata).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void Serializer_metadata_objects_are_rejected_even_for_a_stable_contract_type()
    {
        var builder = Endpoint("POST /stable-json", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(JsonTypeInfo.CreateJsonTypeInfo(typeof(StableRequest), new JsonSerializerOptions()));

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.SerializerMetadata &&
            violation.ArtifactIdentity.Contains("JsonTypeInfo objects are not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void Marker_and_validator_have_null_guards_and_reject_invalid_marker_values()
    {
        Assert.Throws<ArgumentNullException>(() => EndpointLifetimeValidator.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => EndpointLifetimeValidator.ValidateAndMark(null!));
        Assert.Throws<ArgumentNullException>(() => new EndpointLifetimeMetadata(null!, "GET /safe"));
        Assert.Throws<ArgumentException>(() => new EndpointLifetimeMetadata("group", " "));
        Assert.Throws<ArgumentException>(() => new EndpointLifetimeMetadata("group", "GET /safe", default));
    }

    [Fact]
    public void Dynamic_api_explorer_refresh_registration_is_idempotent_and_null_guarded()
    {
        Assert.Throws<ArgumentNullException>(() => new EndpointDataSourceActionDescriptorChangeProvider(null!));
        Assert.Throws<ArgumentNullException>(() => NativeEndpointsServiceCollectionExtensions.AddDynamicEndpointApiExplorerRefresh(null!));

        var source = new TestEndpointDataSource();
        var services = new ServiceCollection();
        services.AddSingleton<EndpointDataSource>(source);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddDynamicEndpointApiExplorerRefresh();
        using var provider = services.AddElsaEndpoints().BuildServiceProvider();

        var changeProvider = Assert.Single(provider.GetServices<IActionDescriptorChangeProvider>());
        Assert.IsType<EndpointDataSourceActionDescriptorChangeProvider>(changeProvider);
        Assert.Same(source.GetChangeToken(), changeProvider.GetChangeToken());
    }

    [Fact]
    public void RequireStableOpenApi_returns_the_same_builder_and_registers_a_final_convention()
    {
        var conventions = new RecordingConventionBuilder();
        var result = conventions.RequireStableEndpointMetadata();

        Assert.Same(conventions, result);
        Assert.Empty(conventions.OrdinaryConventions);
        var builder = Endpoint("GET /safe", EndpointOwnershipMetadata.Host("Elsa.Tests"));
        conventions.FinalConventions.Single()(builder);
        Assert.Single(builder.Metadata.OfType<EndpointLifetimeMetadata>());
    }

    [Fact]
    public void Stable_openapi_convention_removes_compiler_only_handler_metadata_before_validation()
    {
        var conventions = new RecordingConventionBuilder();
        conventions.RequireStableEndpointMetadata();
        var builder = Endpoint("PUT /async", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));
        builder.Metadata.Add(new System.Runtime.CompilerServices.AsyncStateMachineAttribute(CreateCollectibleType("AsyncHandlerStateMachine")));
        builder.Metadata.Add(new System.Diagnostics.DebuggerStepThroughAttribute());

        conventions.FinalConventions.Single()(builder);

        Assert.Empty(builder.Metadata.OfType<System.Runtime.CompilerServices.AsyncStateMachineAttribute>());
        Assert.Empty(builder.Metadata.OfType<System.Diagnostics.DebuggerStepThroughAttribute>());
        Assert.Single(builder.Metadata.OfType<EndpointLifetimeMetadata>());
    }

    [Fact]
    public void Enforcement_is_on_for_a_host_that_configured_nothing()
    {
        // Fail-closed: a host that never registers the options, or exposes no service provider at
        // all, keeps the boundary. Only an explicit, resolvable suppression turns it off.
        var conventions = new RecordingConventionBuilder();
        conventions.RequireStableEndpointMetadata();
        var builder = Endpoint(
            "GET /unconfigured",
            new ServiceCollection().AddElsaEndpoints().BuildServiceProvider(),
            EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));
        builder.Metadata.Add(new AcceptsMetadata(CreateCollectibleType("UnconfiguredRequest")));

        Assert.Throws<UnsafeEndpointMetadataException>(() => conventions.FinalConventions.Single()(builder));
    }

    [Fact]
    public void Suppressed_enforcement_accepts_a_collectible_contract_and_marks_nothing()
    {
        // A host with no OpenAPI document service has no API Explorer cache to retain the type, so
        // the candidate is not rejected. It carries no lifetime marker, because nothing verified it.
        var services = new ServiceCollection();
        services.SuppressEndpointLifetimeEnforcement();
        var conventions = new RecordingConventionBuilder();
        conventions.RequireStableEndpointMetadata();
        var builder = Endpoint(
            "GET /collectible",
            services.AddElsaEndpoints().BuildServiceProvider(),
            EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));
        builder.Metadata.Add(new AcceptsMetadata(CreateCollectibleType("SuppressedRequest")));

        conventions.FinalConventions.Single()(builder);

        Assert.Empty(builder.Metadata.OfType<EndpointLifetimeMetadata>());
    }

    [Fact]
    public void Suppressed_enforcement_still_strips_compiler_only_handler_metadata()
    {
        // An async state machine pins its owner through endpoint metadata whether or not a document
        // is ever produced, so stripping is unconditional while validation is not.
        var services = new ServiceCollection();
        services.SuppressEndpointLifetimeEnforcement();
        var conventions = new RecordingConventionBuilder();
        conventions.RequireStableEndpointMetadata();
        var builder = Endpoint(
            "PUT /suppressed-async",
            services.AddElsaEndpoints().BuildServiceProvider(),
            EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new System.Runtime.CompilerServices.AsyncStateMachineAttribute(CreateCollectibleType("SuppressedStateMachine")));
        builder.Metadata.Add(new System.Diagnostics.DebuggerStepThroughAttribute());

        conventions.FinalConventions.Single()(builder);

        Assert.Empty(builder.Metadata.OfType<System.Runtime.CompilerServices.AsyncStateMachineAttribute>());
        Assert.Empty(builder.Metadata.OfType<System.Diagnostics.DebuggerStepThroughAttribute>());
        Assert.Empty(builder.Metadata.OfType<EndpointLifetimeMetadata>());
    }

    [Fact]
    public void Final_convention_runs_after_metadata_added_by_ordinary_conventions()
    {
        var conventions = new RecordingConventionBuilder();
        conventions.Add(builder => builder.Metadata.Add(new AcceptsMetadata(CreateCollectibleType("RequestAfterConvention"))));
        conventions.RequireStableEndpointMetadata();
        var builder = Endpoint("GET /late", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));

        conventions.OrdinaryConventions.Single()(builder);
        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => conventions.FinalConventions.Single()(builder));

        Assert.Equal(EndpointLifetimeViolationCategory.RequestType, exception.Violation.Category);
        Assert.Contains("group='Elsa.Tests'; endpoint='GET /late'; category=RequestType", exception.Message, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Ownership is Elsa's vocabulary, so once the unload-safety validator moved into
    /// NativeEndpoints the invariant moved with it into <see cref="EndpointOwnershipValidator"/>
    /// rather than out of the tree. It is still enforced as a final convention at mapping time, so
    /// an unowned endpoint still cannot reach the manifest.
    /// </remarks>
    [Fact]
    public void Missing_and_duplicate_ownership_are_rejected_before_publication()
    {
        var missing = Endpoint("GET /missing");
        var missingException = Assert.Throws<UnownedEndpointException>(() => EndpointOwnershipValidator.Validate(missing));
        Assert.Equal(EndpointOwnershipViolationCategory.MissingOwnership, missingException.Category);

        var duplicate = Endpoint("GET /duplicate", EndpointOwnershipMetadata.Module("Elsa.One"));
        duplicate.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.Two"));
        var duplicateException = Assert.Throws<UnownedEndpointException>(() => EndpointOwnershipValidator.Validate(duplicate));
        Assert.Equal(EndpointOwnershipViolationCategory.DuplicateOwnership, duplicateException.Category);
        Assert.Contains("Elsa.One, Elsa.Two", duplicateException.Message, StringComparison.Ordinal);

        var conflictingDynamic = Endpoint(
            "GET /duplicate-dynamic",
            EndpointOwnershipMetadata.DynamicShell("Elsa.One", "shell-a", 1));
        conflictingDynamic.Metadata.Add(EndpointOwnershipMetadata.DynamicShell("Elsa.Two", "shell-b", 2));
        var conflictingException = Assert.Throws<UnownedEndpointException>(() => EndpointOwnershipValidator.Validate(conflictingDynamic));
        Assert.Equal(EndpointOwnershipViolationCategory.DuplicateOwnership, conflictingException.Category);
    }

    [Fact]
    public void An_endpoint_owned_by_exactly_one_module_passes_ownership_validation()
    {
        var owned = Endpoint("GET /owned", EndpointOwnershipMetadata.Module("Elsa.One"));

        Assert.Equal("Elsa.One", EndpointOwnershipValidator.Validate(owned).Owner);
    }

    [Theory]
    [MemberData(nameof(UnsafeMetadataCases))]
    public void Every_collectible_api_explorer_artifact_is_rejected(
        EndpointLifetimeViolationCategory expectedCategory,
        Func<object> metadataFactory)
    {
        var builder = Endpoint("GET /unsafe", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-a", 7));
        builder.Metadata.Add(metadataFactory());

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == expectedCategory &&
            violation.LoadContextIdentity.Contains("collectible", StringComparison.Ordinal));
        Assert.All(exception.Violations, violation =>
        {
            Assert.Equal("Elsa.Tests", violation.Group);
            Assert.Equal("GET /unsafe", violation.Endpoint);
            Assert.DoesNotContain("0x", violation.ArtifactIdentity, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Unknown_metadata_getter_fails_closed_without_exposing_the_object()
    {
        var builder = Endpoint("GET /unknown", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new ThrowingMetadata());

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        var violation = Assert.Single(exception.Violations);
        Assert.Equal(EndpointLifetimeViolationCategory.UnknownMetadataShape, violation.Category);
        Assert.Contains("getter failed", violation.ArtifactIdentity, StringComparison.Ordinal);
        Assert.DoesNotContain("test-only getter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_collectible_metadata_graph_is_rejected()
    {
        var builder = Endpoint("GET /nested", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new NestedMetadata(new NestedMetadataValue(CreateCollectibleType("NestedContract"))));

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.MetadataObject &&
            violation.ArtifactIdentity.Contains("NestedContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_collectible_metadata_graph_is_rejected()
    {
        var builder = Endpoint("GET /private", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new PrivateMetadata(Activator.CreateInstance(CreateCollectibleType("PrivateContract"))!));

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.MetadataObject &&
            violation.ArtifactIdentity.Contains("PrivateContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Stable_generic_metadata_with_collectible_type_argument_is_rejected()
    {
        var builder = Endpoint("GET /generic", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        var metadataType = typeof(GenericMetadata<>).MakeGenericType(CreateCollectibleType("GenericContract"));
        builder.Metadata.Add(Activator.CreateInstance(metadataType)!);

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.MetadataObject &&
            violation.ArtifactIdentity.Contains("GenericContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Stable_delegate_target_with_private_collectible_state_is_rejected()
    {
        var builder = Endpoint("GET /captured", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        var target = new StableCallbackTarget(Activator.CreateInstance(CreateCollectibleType("CapturedContract"))!);
        builder.Metadata.Add((Action)target.Invoke);

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.DelegateOrTransformer &&
            violation.ArtifactIdentity.Contains("CapturedContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_multicast_delegate_invocation_is_inspected()
    {
        var builder = Endpoint("GET /multicast", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        var collectible = (Action)CreateCollectibleDelegate();
        Action stable = StableCallback;
        builder.Metadata.Add(collectible + stable);

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.DelegateOrTransformer &&
            violation.ArtifactIdentity.Contains("CollectibleMethod", StringComparison.Ordinal));
    }

    [Fact]
    public void Unbounded_metadata_enumeration_fails_closed()
    {
        var builder = Endpoint("GET /enumeration", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(Enumerable.Repeat<object>("value", 257));

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.UnknownMetadataShape &&
            violation.ArtifactIdentity.Contains("enumeration exceeds 256 items", StringComparison.Ordinal));
    }

    [Fact]
    public void Enumerable_private_collectible_state_is_rejected_after_yielded_values_are_inspected()
    {
        var builder = Endpoint("GET /enumerable-state", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new StableEnumerableMetadata(Activator.CreateInstance(CreateCollectibleType("EnumerableContract"))!));

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.MetadataObject &&
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

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.MemberOrMethod &&
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

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(builder));

        Assert.Contains(exception.Violations, violation =>
            violation.Category == EndpointLifetimeViolationCategory.DelegateOrTransformer &&
            violation.ArtifactIdentity.Contains("DelegateContract", StringComparison.Ordinal));
    }

    [Fact]
    public void Conflicting_existing_lifetime_marker_fails_closed()
    {
        var builder = Endpoint("GET /marker", EndpointOwnershipMetadata.Module("Elsa.Tests"));
        builder.Metadata.Add(new EndpointLifetimeMetadata("Elsa.Other", "GET /other"));

        var exception = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.ValidateAndMark(builder));

        Assert.Equal(EndpointLifetimeViolationCategory.UnknownMetadataShape, exception.Violation.Category);
        Assert.Contains("lifetime marker", exception.Violation.ArtifactIdentity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostics_are_group_aware_and_stable_across_repeated_validation()
    {
        var collectibleType = CreateCollectibleType("StableDiagnosticRequest");
        var first = Endpoint("GET /diagnostic", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-b", 11));
        first.Metadata.Add(new ProducesMetadata(collectibleType, StatusCodes.Status200OK));
        first.Metadata.Add(new AcceptsMetadata(collectibleType));
        var second = Endpoint("GET /diagnostic", EndpointOwnershipMetadata.DynamicShell("Elsa.Tests", "shell-b", 11));
        second.Metadata.Add(new ProducesMetadata(collectibleType, StatusCodes.Status200OK));
        second.Metadata.Add(new AcceptsMetadata(collectibleType));

        var firstException = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(first));
        var secondException = Assert.Throws<UnsafeEndpointMetadataException>(() => EndpointLifetimeValidator.Validate(second));

        Assert.Equal(firstException.Message, secondException.Message);
        Assert.Equal(
            firstException.Violations.Select(violation => violation.Category),
            firstException.Violations.Select(violation => violation.Category).OrderBy(category => category));
        Assert.Contains("group='Elsa.Tests'; endpoint='GET /diagnostic'", firstException.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> UnsafeMetadataCases()
    {
        yield return [EndpointLifetimeViolationCategory.RequestType, () => new AcceptsMetadata(CreateCollectibleType("RequestContract"))];
        yield return [EndpointLifetimeViolationCategory.ResponseType, () => new ProducesMetadata(CreateCollectibleType("ResponseContract"), StatusCodes.Status200OK)];
        yield return [EndpointLifetimeViolationCategory.MetadataObject, () => Activator.CreateInstance(CreateCollectibleType("MetadataObject"))!];
        yield return [EndpointLifetimeViolationCategory.MemberOrMethod, () => CreateCollectibleMethod()];
        yield return [EndpointLifetimeViolationCategory.DelegateOrTransformer, () => CreateCollectibleDelegate()];
        yield return [EndpointLifetimeViolationCategory.SerializerMetadata, () => JsonTypeInfo.CreateJsonTypeInfo(CreateCollectibleType("SerializerContract"), new JsonSerializerOptions())];
    }

    private static TestEndpointBuilder Endpoint(string displayName, params EndpointOwnershipMetadata[] ownership)
    {
        var builder = new TestEndpointBuilder { DisplayName = displayName };
        foreach (var metadata in ownership)
            builder.Metadata.Add(metadata);
        AddGroup(builder, ownership);
        return builder;
    }

    private static TestEndpointBuilder Endpoint(
        string displayName,
        IServiceProvider services,
        params EndpointOwnershipMetadata[] ownership)
    {
        var builder = new TestEndpointBuilder { DisplayName = displayName, ApplicationServices = services };
        foreach (var metadata in ownership)
            builder.Metadata.Add(metadata);
        AddGroup(builder, ownership);
        return builder;
    }

    /// <remarks>
    /// The lifetime validator names the group, not the owner: group membership is what it can see,
    /// and ownership is Elsa's own vocabulary, enforced separately by
    /// <see cref="EndpointOwnershipValidator"/>. Elsa maps every group under its owner id, so the two
    /// carry the same string and the helper keeps that true here too.
    /// </remarks>
    private static void AddGroup(TestEndpointBuilder builder, EndpointOwnershipMetadata[] ownership)
    {
        if (ownership.Length == 1)
            builder.Metadata.Add(new EndpointGroupMetadata(ownership[0].OwnerId));
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
