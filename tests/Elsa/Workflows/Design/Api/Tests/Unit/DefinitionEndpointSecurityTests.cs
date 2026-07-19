using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
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
    private const string VersionsNamespace = "Elsa.Workflows.Design.Api.Endpoints.Versions";

    [Fact]
    public void Get_endpoint_requires_a_permission_and_is_not_anonymous() =>
        AssertPermissionGuarded($"{EndpointsNamespace}.Get", new StubRequestSender());

    [Fact]
    public void Update_endpoint_the_draft_mutation_gate_requires_a_permission_and_is_not_anonymous() =>
        AssertPermissionGuarded($"{EndpointsNamespace}.Update", new StubCommandSender());

    [Fact]
    public void Delete_endpoint_requires_a_permission_and_is_not_anonymous() =>
        AssertPermissionGuarded($"{EndpointsNamespace}.Delete", new StubCommandSender());

    [Fact]
    public void Tag_read_and_replace_endpoints_require_their_distinct_permissions()
    {
        var service = RuntimeHelpers.GetUninitializedObject(typeof(WorkflowDefinitionTagApplicationService));
        var get = ConfiguredDefinition($"{EndpointsNamespace}.GetTags", service, includeLogger: false);
        var replace = ConfiguredDefinition($"{EndpointsNamespace}.ReplaceTags", service, includeLogger: false);

        Assert.Contains(PermissionNames.WorkflowDesignRead, get.AllowedPermissions!);
        Assert.Contains(PermissionNames.WorkflowDesignTagsAssign, replace.AllowedPermissions!);
        Assert.Null(get.AnonymousVerbs);
        Assert.Null(replace.AnonymousVerbs);
    }

    // D5 sweep (QA finding): endpoints that carried an interim AllowAnonymous() are now permission-guarded.
    // These are the direct regression guards proving the sweep replaced AllowAnonymous() with
    // ConfigurePermissions() and that no swept endpoint can silently regress to anonymous.
    [Theory]
    [InlineData("List", false)]
    [InlineData("Add", true)]
    [InlineData("Submit", true)]
    public void Swept_definition_endpoints_require_a_permission_and_are_not_anonymous(string name, bool isCommand) =>
        AssertPermissionGuarded($"{EndpointsNamespace}.{name}", isCommand ? new StubCommandSender() : new StubRequestSender());

    [Theory]
    [InlineData("List", false)]
    [InlineData("Get", false)]
    [InlineData("Add", true)]
    public void Swept_version_endpoints_require_a_permission_and_are_not_anonymous(string name, bool isCommand) =>
        AssertPermissionGuarded($"{VersionsNamespace}.{name}", isCommand ? new StubCommandSender() : new StubRequestSender());

    private static void AssertPermissionGuarded(string endpointTypeName, object sender)
    {
        var definition = ConfiguredDefinition(endpointTypeName, sender);

        Assert.NotNull(definition.AllowedPermissions);
        Assert.Contains(PermissionNames.All, definition.AllowedPermissions!);
        Assert.Null(definition.AnonymousVerbs);
    }

    private static EndpointDefinition ConfiguredDefinition(
        string endpointTypeName,
        object sender,
        bool includeLogger = true)
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
        var dependencies = includeLogger ? new[] { sender, logger } : [sender];
        var endpoint = (BaseEndpoint)create.Invoke(null, [noopContext, dependencies])!;

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
