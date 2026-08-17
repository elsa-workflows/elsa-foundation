using System.Reflection;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Api.Handlers;
using Elsa.Expressions.Api.Authorization;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Api.Tests;

public sealed class ExpressionDescriptorEndpointTests
{
    private static readonly Assembly ApiAssembly = Assembly.Load("Elsa.Expressions.Api");

    [Theory]
    [InlineData("expressions/descriptors")]
    [InlineData("expressions/variable-types")]
    public void Expressions_api_owns_the_canonical_secured_descriptor_route(string route)
    {
        var endpoint = FindEndpoint(route);

        var owner = Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>());
        Assert.Equal("Elsa.Expressions.Api", owner.OwnerId);
        Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(
            endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        Assert.Equal(EndpointSecurityDispositionKind.Permission, security.Kind);
        var policy = new PermissionPolicyCodec().Parse(security.Value!);
        Assert.Contains(PermissionKey.Normalize(ExpressionsPermissions.Read), policy.Descriptor!.Permissions);
        Assert.DoesNotContain(endpoint.Metadata, item => item is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute);
    }

    [Fact]
    public void Expression_descriptor_contract_preserves_semantic_editing_modes()
    {
        var item = typeof(ExpressionDescriptorsResponse).GetProperty(nameof(ExpressionDescriptorsResponse.Items))!.PropertyType
            .GetGenericArguments()[0];

        AssertProperties(item, "Type", "DisplayName", "Description", "EditingMode");
        Assert.Equal(new[] { "Literal", "Text", "Structured", "Reference" }, Enum.GetNames(item.GetProperty("EditingMode")!.PropertyType));
    }

    [Fact]
    public async Task Expression_descriptor_response_includes_intrinsic_authoring_modes_without_runtime_descriptors()
    {
        var handler = new ListExpressionDescriptorsRequestHandler(new StubExpressionDescriptorRegistry([]));

        var response = await handler.Handle(new ListExpressionDescriptors(), CancellationToken.None);

        Assert.Collection(
            response.Items,
            item => Assert.Equal(new ExpressionDescriptorView("Input", "Input", null, ExpressionEditingModeView.Reference), item),
            item => Assert.Equal(new ExpressionDescriptorView("Literal", "Literal", null, ExpressionEditingModeView.Literal), item),
            item => Assert.Equal(new ExpressionDescriptorView("Object", "Object", null, ExpressionEditingModeView.Structured), item));
    }

    [Fact]
    public async Task Intrinsic_authoring_modes_win_contributed_type_collisions_and_each_type_is_emitted_once()
    {
        var descriptors = new IExpressionDescriptor[]
        {
            Descriptor("Literal", "Contributed Literal", ExpressionEditingMode.Reference),
            Descriptor("Custom", "Custom first", ExpressionEditingMode.Text),
            Descriptor("Custom", "Custom second", ExpressionEditingMode.Reference)
        };
        var handler = new ListExpressionDescriptorsRequestHandler(new StubExpressionDescriptorRegistry(descriptors));

        var response = await handler.Handle(new ListExpressionDescriptors(), CancellationToken.None);

        Assert.Equal(new[] { "Custom", "Input", "Literal", "Object" }, response.Items.Select(x => x.Type));
        Assert.Equal("Custom first", response.Items.Single(x => x.Type == "Custom").DisplayName);
        Assert.Equal(
            new ExpressionDescriptorView("Literal", "Literal", null, ExpressionEditingModeView.Literal),
            response.Items.Single(x => x.Type == "Literal"));
    }

    [Fact]
    public void Variable_type_contract_excludes_runtime_clr_type_details()
    {
        var item = typeof(VariableTypeDescriptorsResponse).GetProperty(nameof(VariableTypeDescriptorsResponse.Items))!.PropertyType
            .GetGenericArguments()[0];

        AssertProperties(item, "Alias", "DisplayName", "Category", "DefaultEditor");
        Assert.Null(item.GetProperty("ClrType"));
    }

    [Fact]
    public void Descriptor_handlers_project_the_active_shell_registries()
    {
        AssertHandlerDependency(
            "Elsa.Expressions.Api.Handlers.ListExpressionDescriptorsRequestHandler",
            "Elsa.Expressions.Core.Contracts.IExpressionDescriptorRegistry");
        AssertHandlerDependency(
            "Elsa.Expressions.Api.Handlers.ListVariableTypeDescriptorsRequestHandler",
            "Elsa.Expressions.Core.Contracts.IVariableTypeDescriptorCatalog");
    }

    private static RouteEndpoint FindEndpoint(string route)
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        ExpressionsApi.MapExpressionsApi(routes);
        var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.RoutePattern.RawText == route);
        Assert.NotNull(endpoint);
        return endpoint!;
    }

    private static void AssertHandlerDependency(string typeName, string dependencyName)
    {
        var handler = ApiAssembly.GetType(typeName);
        Assert.NotNull(handler);
        Assert.Contains(handler!.GetConstructors().Single().GetParameters(), parameter => parameter.ParameterType.FullName == dependencyName);
    }

    private static ExpressionDescriptor Descriptor(string type, string displayName, ExpressionEditingMode editingMode) =>
        new(type, editingMode) { DisplayName = displayName };

    private static void AssertProperties(Type type, params string[] names) =>
        Assert.All(names, name => Assert.NotNull(type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)));

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class StubExpressionDescriptorRegistry(IEnumerable<IExpressionDescriptor> descriptors) : IExpressionDescriptorRegistry
    {
        private readonly List<IExpressionDescriptor> _descriptors = [.. descriptors];

        public void Add(IExpressionDescriptor descriptor) => _descriptors.Add(descriptor);
        public void AddRange(IEnumerable<IExpressionDescriptor> descriptors) => _descriptors.AddRange(descriptors);
        public IEnumerable<IExpressionDescriptor> ListAll() => _descriptors;
        public IExpressionDescriptor? Find(Func<IExpressionDescriptor, bool> predicate) => _descriptors.FirstOrDefault(predicate);
        public IExpressionDescriptor? Find(string type) => _descriptors.FirstOrDefault(x => x.TypeName == type);
    }
}
