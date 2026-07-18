using System.Reflection;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Proves every Activities.Design.Api endpoint is permission-guarded by construction and never
/// relaxes to anonymous — the Activities-side counterpart of the Workflows.Design.Api
/// <c>DefinitionEndpointSecurityTests</c> (W29: with both suites, all 18 design endpoint files are
/// pinned). The endpoints are <c>internal</c>, so they are resolved by name and built through
/// FastEndpoints' <c>Factory.Create</c> via reflection — the same build path the framework uses —
/// rather than adding a forbidden <c>InternalsVisibleTo</c> (constitution §2.23.3). Constructor
/// dependencies (sender + typed logger) are supplied from the endpoint's own constructor signature,
/// which also covers the <c>ListVersions</c> endpoint's <c>ILogger&lt;List&gt;</c> quirk.
/// </summary>
public sealed class ActivityDesignEndpointSecurityTests
{
    private const string Root = "Elsa.Activities.Design.Api.Endpoints";

    [Theory]
    [InlineData("Availability.GetSettings")]
    [InlineData("Availability.ListDiagnostics")]
    [InlineData("Availability.SaveSettings")]
    [InlineData("AuthoringCapabilities.Get")]
    [InlineData("Catalog.List")]
    [InlineData("Definitions.Add")]
    [InlineData("Definitions.AddDraft")]
    [InlineData("Definitions.PreviewFork")]
    [InlineData("Forks.Apply")]
    [InlineData("Forks.GetStatus")]
    [InlineData("Definitions.Get")]
    [InlineData("Definitions.List")]
    [InlineData("Definitions.ListDrafts")]
    [InlineData("Definitions.ListVersions")]
    [InlineData("Definitions.Recommendation")]
    [InlineData("Definitions.Picker")]
    [InlineData("Definitions.Update")]
    [InlineData("Drafts.Diff")]
    [InlineData("Drafts.ApplyContractProposal")]
    [InlineData("Drafts.ConflictCopy")]
    [InlineData("Drafts.Discard")]
    [InlineData("Drafts.Get")]
    [InlineData("Drafts.MigrateProvider")]
    [InlineData("Drafts.ProposeContract")]
    [InlineData("Drafts.Replace")]
    [InlineData("Drafts.UpdatePresentation")]
    [InlineData("Drafts.Validate")]
    [InlineData("Versions.Diff")]
    [InlineData("Versions.Dependencies")]
    [InlineData("Versions.Get")]
    [InlineData("Versions.Restore")]
    [InlineData("Versions.Retire")]
    [InlineData("Versions.Revoke")]
    public void Endpoint_requires_a_permission_and_is_not_anonymous(string relativeTypeName)
    {
        var definition = ConfiguredDefinition($"{Root}.{relativeTypeName}");

        Assert.NotNull(definition.AllowedPermissions);
        Assert.Contains(PermissionNames.All, definition.AllowedPermissions!);
        Assert.Null(definition.AnonymousVerbs);
    }

    [Fact]
    public void The_inline_list_covers_every_endpoint_in_the_assembly()
    {
        // Guards the theory data itself: a newly added Activities.Design.Api endpoint must be added
        // above (and thereby pinned as permission-guarded) before this suite goes green again.
        var endpointTypes = typeof(AddDefinition).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Namespace?.StartsWith(Root, StringComparison.Ordinal) == true
                        && typeof(BaseEndpoint).IsAssignableFrom(t))
            .Select(t => t.FullName![(Root.Length + 1)..])
            .OrderBy(n => n, StringComparer.Ordinal);

        string[] covered =
        [
            "Availability.GetSettings",
            "Availability.ListDiagnostics",
            "Availability.SaveSettings",
            "AuthoringCapabilities.Get",
            "Catalog.List",
            "Definitions.Add",
            "Definitions.AddDraft",
            "Definitions.PreviewFork",
            "Definitions.Get",
            "Definitions.List",
            "Definitions.ListDrafts",
            "Definitions.ListVersions",
            "Definitions.Recommendation",
            "Definitions.Picker",
            "Definitions.Update",
            "Drafts.Diff",
            "Drafts.ApplyContractProposal",
            "Drafts.ConflictCopy",
            "Drafts.Discard",
            "Drafts.Get",
            "Drafts.MigrateProvider",
            "Drafts.ProposeContract",
            "Drafts.Replace",
            "Drafts.UpdatePresentation",
            "Drafts.Validate",
            "Forks.Apply",
            "Forks.GetStatus",
            "UpgradePlans.Apply",
            "UpgradePlans.Create",
            "UpgradePlans.Get",
            "Versions.Diff",
            "Versions.Dependencies",
            "Versions.Get",
            "Versions.Restore",
            "Versions.Retire",
            "Versions.Revoke"
        ];

        Assert.Equal(covered.OrderBy(n => n, StringComparer.Ordinal), endpointTypes);
    }

    private static EndpointDefinition ConfiguredDefinition(string endpointTypeName)
    {
        var endpointType = typeof(AddDefinition).Assembly.GetType(endpointTypeName, throwOnError: true)!;

        // Supply each constructor dependency from the signature itself: the sender stub by contract,
        // the logger by its declared generic argument (not the endpoint type — see ListVersions).
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(p => ResolveDependency(p.ParameterType))
            .ToArray();

        var create = typeof(Factory).GetMethods()
            .Single(m => m.Name == nameof(Factory.Create)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters() is [var first, var rest]
                         && first.ParameterType == typeof(Action<DefaultHttpContext>)
                         && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        Action<DefaultHttpContext> noopContext = _ => { };
        var endpoint = (BaseEndpoint)create.Invoke(null, [noopContext, dependencies])!;

        endpoint.Configure();
        return endpoint.Definition;
    }

    private static object ResolveDependency(Type parameterType)
    {
        if (parameterType == typeof(IRequestSender))
            return new StubRequestSender();
        if (parameterType == typeof(ICommandSender))
            return new StubCommandSender();
        if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
        {
            var nullLoggerType = typeof(NullLogger<>).MakeGenericType(parameterType.GetGenericArguments()[0]);
            return (nullLoggerType.GetProperty("Instance")?.GetValue(null)
                    ?? nullLoggerType.GetField("Instance")?.GetValue(null))!;
        }

        throw new InvalidOperationException($"Unexpected endpoint constructor dependency '{parameterType}'.");
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
