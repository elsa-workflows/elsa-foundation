using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Proves the three implemented DS-5/DS-6 definition endpoints are permission-guarded by construction (W4):
/// each <c>Configure()</c> applies the <c>*</c> permission through <c>ConfigurePermissions()</c> and none of
/// them relaxes to anonymous. This is the regression guard against a future edit swapping the mutation gate
/// (<c>Update</c>) to <c>AllowAnonymous()</c>. The endpoints are <c>internal</c> (Design.Api convention), so
/// they are resolved by name and built through FastEndpoints' <c>Factory.Create</c> via reflection — the same
/// build path the framework uses — rather than adding a forbidden <c>InternalsVisibleTo</c> (constitution
/// §2.23.3). The configured <see cref="EndpointDefinition"/> is then inspected directly, mirroring
/// <c>ApiSecurityConfiguratorTests</c>.
/// </summary>
public sealed class DefinitionEndpointSecurityTests
{
    private const string EndpointsNamespace = "Elsa.Workflows.Design.Api.Endpoints.Definitions";

    [Fact]
    public void Get_endpoint_requires_a_permission_and_is_not_anonymous() =>
        AssertPermissionGuarded($"{EndpointsNamespace}.Get", new StubRequestSender());

    [Fact]
    public void Update_endpoint_the_draft_mutation_gate_requires_a_permission_and_is_not_anonymous() =>
        AssertPermissionGuarded($"{EndpointsNamespace}.Update", new StubCommandSender());

    [Fact]
    public void Delete_endpoint_requires_a_permission_and_is_not_anonymous() =>
        AssertPermissionGuarded($"{EndpointsNamespace}.Delete", new StubCommandSender());

    private static void AssertPermissionGuarded(string endpointTypeName, object sender)
    {
        var definition = ConfiguredDefinition(endpointTypeName, sender);

        Assert.NotNull(definition.AllowedPermissions);
        Assert.Contains(PermissionNames.All, definition.AllowedPermissions!);
        Assert.Null(definition.AnonymousVerbs);
    }

    private static EndpointDefinition ConfiguredDefinition(string endpointTypeName, object sender)
    {
        var endpointType = typeof(UpdateDefinition).Assembly.GetType(endpointTypeName, throwOnError: true)!;
        var nullLoggerType = typeof(NullLogger<>).MakeGenericType(endpointType);
        var logger = (nullLoggerType.GetProperty("Instance")?.GetValue(null)
                      ?? nullLoggerType.GetField("Instance")?.GetValue(null))!;

        // Factory.Create<TEndpoint>(Action<DefaultHttpContext>, params object[] deps) — the same overload the
        // Runtime endpoint tests use, invoked generically because the endpoint type is internal.
        var create = typeof(Factory).GetMethods()
            .Single(m => m.Name == nameof(Factory.Create)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters() is [var first, var rest]
                         && first.ParameterType == typeof(Action<DefaultHttpContext>)
                         && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        Action<DefaultHttpContext> noopContext = _ => { };
        var endpoint = (BaseEndpoint)create.Invoke(null, [noopContext, new[] { sender, logger }])!;

        endpoint.Configure();
        return endpoint.Definition;
    }

    private sealed class StubRequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
            where T : notnull =>
            throw new InvalidOperationException("Handler must not be invoked in a configuration-only test.");
    }

    private sealed class StubCommandSender : ICommandSender
    {
        public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default)
            where T : notnull =>
            throw new InvalidOperationException("Handler must not be invoked in a configuration-only test.");

        public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Handler must not be invoked in a configuration-only test.");
    }
}
