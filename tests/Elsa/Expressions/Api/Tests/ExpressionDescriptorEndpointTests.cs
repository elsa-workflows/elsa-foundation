using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Api.Handlers;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Expressions.Api.Tests;

/// <summary>RED contract for the reference-server descriptor projections moving into Expressions API.</summary>
public sealed class ExpressionDescriptorEndpointTests
{
    private static readonly Assembly ApiAssembly = Assembly.Load("Elsa.Expressions.Api");

    [Theory]
    [InlineData("expressions/descriptors")]
    [InlineData("expressions/variable-types")]
    public void Expressions_api_owns_the_canonical_secured_descriptor_route(string route)
    {
        var endpoint = FindEndpoint(route);

        Assert.NotEmpty(endpoint.Definition.PreBuiltUserPolicies!);
        Assert.All(
            endpoint.Definition.PreBuiltUserPolicies!,
            policy => Assert.Equal(ElsaEndpointPermissions.ComposePolicy([PermissionNames.ExpressionsRead]), policy));
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Expression_descriptor_contract_preserves_semantic_editing_modes()
    {
        var item = ResponseItemType(FindEndpoint("expressions/descriptors"));

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
        var item = ResponseItemType(FindEndpoint("expressions/variable-types"));

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

    private static void AssertHandlerDependency(string typeName, string dependencyName)
    {
        var handler = ApiAssembly.GetType(typeName);
        Assert.NotNull(handler);
        Assert.Contains(handler!.GetConstructors().Single().GetParameters(), parameter => parameter.ParameterType.FullName == dependencyName);
    }

    private static BaseEndpoint FindEndpoint(string route)
    {
        var endpoint = ApiAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(BaseEndpoint).IsAssignableFrom(type))
            .Select(CreateEndpoint)
            .SingleOrDefault(candidate => candidate.Definition.Routes.Contains(route, StringComparer.Ordinal));
        Assert.NotNull(endpoint);
        return endpoint;
    }

    private static Type ResponseItemType(BaseEndpoint endpoint)
    {
        var response = ResponseContract(endpoint);
        var items = response.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(items);
        return CollectionElementType(items!.PropertyType);
    }

    private static Type ResponseContract(BaseEndpoint endpoint)
    {
        for (var current = endpoint.GetType().BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Elsa.Api.FastEndpoints.Abstractions.ElsaEndpointWithoutRequest<>))
                return current.GenericTypeArguments[0];
        }
        throw new InvalidOperationException($"Endpoint '{endpoint.GetType().FullName}' has no response contract.");
    }

    private static BaseEndpoint CreateEndpoint(Type endpointType)
    {
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single().GetParameters().Select(parameter => ResolveDependency(parameter.ParameterType)).ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), dependencies])!;
        endpoint.Configure();
        return endpoint;
    }

    private static object ResolveDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var nullLogger = typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]);
            return (nullLogger.GetProperty("Instance")?.GetValue(null) ?? nullLogger.GetField("Instance")?.GetValue(null))!;
        }
        if (type.IsInterface)
            return DispatchProxy.Create(type, typeof(NoopProxy));
        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static Type CollectionElementType(Type collectionType) => collectionType.GetInterfaces().Append(collectionType)
        .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];

    private static void AssertProperties(Type type, params string[] names) =>
        Assert.All(names, name => Assert.NotNull(type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)));

    private static ExpressionDescriptor Descriptor(string type, string displayName, ExpressionEditingMode editingMode) =>
        new(type, editingMode) { DisplayName = displayName };

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
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
