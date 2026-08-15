using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Xunit;

namespace Elsa.Api.Compatibility.Testing.Tests;

public sealed class EndpointMetadataConventionTests
{
    [Fact]
    public void Owner_and_public_disposition_are_published_as_standard_metadata()
    {
        var builder = new RecordingConventionBuilder();

        builder.WithOwner("Elsa.Workflows").AllowPublic("health", "Required for unauthenticated readiness probes.");
        var metadata = builder.BuildMetadata();

        var ownership = Assert.Single(metadata.OfType<EndpointOwnershipMetadata>());
        Assert.Equal((EndpointOwnerKind.Module, "Elsa.Workflows", null, null),
            (ownership.Kind, ownership.OwnerId, ownership.ShellId, ownership.Generation));
        var disposition = Assert.Single(metadata.OfType<EndpointSecurityDispositionMetadata>());
        Assert.Equal((EndpointSecurityDispositionKind.Public, "health", "Required for unauthenticated readiness probes."),
            (disposition.Kind, disposition.Category, disposition.Reason));
        Assert.Single(metadata.OfType<IAllowAnonymous>());
    }

    [Fact]
    public void Host_credentials_and_named_policies_keep_their_typed_ownership()
    {
        var host = new RecordingConventionBuilder();
        host.RequireHostCredential("Bearer", "Foundation.Host");
        var policy = new RecordingConventionBuilder();
        policy.RequireNamedPolicy("workflow.read", "Elsa.Workflows");

        var hostMetadata = Assert.Single(host.BuildMetadata().OfType<EndpointSecurityDispositionMetadata>());
        var policyMetadata = Assert.Single(policy.BuildMetadata().OfType<EndpointSecurityDispositionMetadata>());
        Assert.Equal((EndpointSecurityDispositionKind.HostCredential, "Bearer", "Foundation.Host"),
            (hostMetadata.Kind, hostMetadata.Value, hostMetadata.Owner));
        Assert.Equal((EndpointSecurityDispositionKind.NamedPolicy, "workflow.read", "Elsa.Workflows"),
            (policyMetadata.Kind, policyMetadata.Value, policyMetadata.Owner));
        Assert.Equal("Bearer", Assert.Single(host.BuildMetadata().OfType<IAuthorizeData>()).AuthenticationSchemes);
        Assert.Equal("workflow.read", Assert.Single(policy.BuildMetadata().OfType<IAuthorizeData>()).Policy);
    }

    [Fact]
    public void Host_and_dynamic_shell_ownership_keep_lifecycle_identity()
    {
        var host = new RecordingConventionBuilder();
        host.WithHostOwner("Elsa.Foundation.Host");
        var shell = new RecordingConventionBuilder();
        shell.WithDynamicShellOwner("feature:orders", "tenant-a", 7);

        var hostMetadata = Assert.Single(host.BuildMetadata().OfType<EndpointOwnershipMetadata>());
        var shellMetadata = Assert.Single(shell.BuildMetadata().OfType<EndpointOwnershipMetadata>());
        Assert.Equal((EndpointOwnerKind.Host, "Elsa.Foundation.Host", null, null),
            (hostMetadata.Kind, hostMetadata.OwnerId, hostMetadata.ShellId, hostMetadata.Generation));
        Assert.Equal((EndpointOwnerKind.DynamicShell, "feature:orders", "tenant-a", 7),
            (shellMetadata.Kind, shellMetadata.OwnerId, shellMetadata.ShellId, shellMetadata.Generation));
    }

    [Fact]
    public void Undefined_security_disposition_kinds_are_rejected()
    {
        var undefined = (EndpointSecurityDispositionKind)999;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EndpointSecurityDispositionMetadata(undefined));
        Assert.Throws<ArgumentException>(() => EndpointSecurityDispositionMetadata.Public("health", " "));
        Assert.Throws<ArgumentException>(() => EndpointOwnershipMetadata.DynamicShell("orders", " ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EndpointOwnershipMetadata.DynamicShell("orders", "shell", -1));
    }

    private sealed class RecordingConventionBuilder : IEndpointConventionBuilder
    {
        private readonly List<Action<EndpointBuilder>> _conventions = [];
        public ICollection<Action<EndpointBuilder>> Conventions => _conventions;
        public void Add(Action<EndpointBuilder> convention) => _conventions.Add(convention);
        public IReadOnlyList<object> BuildMetadata()
        {
            var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/test"), 0);
            foreach (var convention in _conventions)
                convention(endpoint);
            return endpoint.Metadata.ToArray();
        }
    }
}
