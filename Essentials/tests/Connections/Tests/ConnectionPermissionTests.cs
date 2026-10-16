using System.Security.Claims;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Connections.Api.Services;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Core.Permissions;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class ConnectionPermissionTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task Resolve_RequiresUsePermission()
    {
        await using var provider = CreateProvider(new RecordingGate { DenyUse = true });
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectionResolver>();

        await manager.CreateAsync(Tenant, BearerRequest("gated"));
        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = Tenant,
            Name = "gated"
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, refused.Code);
    }

    [Fact]
    public async Task Apply_OnAPublishedBinding_DoesNotRequireTheRunTriggerToHoldUse()
    {
        await using var provider = CreateProvider(new RecordingGate { DenyUse = true }, workflows: true);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var applicator = scope.ServiceProvider.GetRequiredService<IHttpConnectionApplicator>();

        await manager.CreateAsync(Tenant, BearerRequest("published-apply"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/v1");
        await applicator.ApplyAsync(
            request,
            new ConnectionReference("published-apply", "http"),
            new ActivityExecutionContext("wf", "actexec-1", "attempt-1", "node-1", CancellationToken.None));
        Assert.True(request.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task Apply_RefusesABlankBoundName()
    {
        await using var provider = CreateProvider(new RecordingGate(), workflows: true);
        await using var scope = provider.CreateAsyncScope();
        var applicator = scope.ServiceProvider.GetRequiredService<IHttpConnectionApplicator>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/v1");
        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => applicator.ApplyAsync(
            request,
            new ConnectionReference { Name = "  ", Type = "http" },
            new ActivityExecutionContext("wf", "actexec-1", "attempt-1", "node-1", CancellationToken.None)).AsTask());
        Assert.Equal(ConnectionFaultCodes.Unavailable, refused.Code);
        Assert.False(request.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task BindingAnExistingSecret_RequiresBindSecretPermission()
    {
        await using var provider = CreateProvider(new RecordingGate { DenyBindSecret = true });
        await using var scope = provider.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "existing-token",
            Value = "tok",
            TypeName = SecretTypeNames.Text
        });

        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "bound",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "existing-token" }
            }
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, refused.Code);
    }

    [Fact]
    public async Task BindingANamedCredential_RequiresBindSecretPermission()
    {
        await using var provider = CreateProvider(new RecordingGate { DenyBindSecret = true });
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "named",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new()
                {
                    SourceKind = ConnectionCredentialSourceKinds.NamedCredential,
                    NamedCredentialName = "payments"
                }
            }
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, refused.Code);
    }

    [Fact]
    public async Task AnOwnedSecretValue_DoesNotRequireBindSecretPermission()
    {
        var gate = new RecordingGate { DenyBindSecret = true };
        await using var provider = CreateProvider(gate);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        var created = await manager.CreateAsync(Tenant, BearerRequest("owned"));
        Assert.Equal("owned", created.Name);
        Assert.Empty(gate.BindCalls);
    }

    [Fact]
    public async Task UpdatingHostsOnABoundExistingSecret_RequiresBindSecretPermission()
    {
        var gate = new RecordingGate();
        await using var provider = CreateProvider(gate);
        await using var scope = provider.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "existing-token",
            Value = "tok",
            TypeName = SecretTypeNames.Text
        });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "retarget",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "existing-token" }
            }
        });

        gate.DenyBindSecret = true;
        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => manager.UpdateAsync(Tenant, "retarget", new UpdateConnectionRequest
        {
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "other.example" }
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, refused.Code);
    }

    [Fact]
    public async Task UpdatingOnlyThePortOnABoundConnection_RequiresBindSecretPermission()
    {
        var gate = new RecordingGate();
        await using var provider = CreateProvider(gate);
        await using var scope = provider.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "existing-token",
            Value = "tok",
            TypeName = SecretTypeNames.Text
        });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "port-retarget",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.BaseUrl] = "https://api.example/v1" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "existing-token" }
            }
        });

        gate.DenyBindSecret = true;
        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => manager.UpdateAsync(Tenant, "port-retarget", new UpdateConnectionRequest
        {
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.BaseUrl] = "https://api.example:8443/v1" }
        }).AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, refused.Code);
    }

    [Fact]
    public async Task Test_RequiresTestPermission_AndDoesNotTreatItAsUse()
    {
        var gate = new RecordingGate { DenyTest = true };
        await using var provider = CreateProvider(gate);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await manager.CreateAsync(Tenant, BearerRequest("needs-test"));
        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => manager.TestAsync(Tenant, "needs-test").AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, refused.Code);
        Assert.Equal([ConnectionsPermissions.Test], gate.TestCalls);
        Assert.Empty(gate.UseCalls);
    }

    [Fact]
    public async Task DeleteAndRevoke_RequireManagePermission()
    {
        var gate = new RecordingGate { DenyManage = true };
        await using var provider = CreateProvider(gate);
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        var delete = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => manager.DeleteAsync(Tenant, "any").AsTask());
        var revoke = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => manager.RevokeAsync(Tenant, "any").AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, delete.Code);
        Assert.Equal(ConnectionFaultCodes.Forbidden, revoke.Code);
    }

    [Fact]
    public async Task HttpContextGate_RefusesWhenThereIsNoPrincipal()
    {
        var authorization = new FakeAuthorization();
        var accessor = new HttpContextAccessor();
        var gate = new HttpContextConnectionPermissionGate(authorization, accessor);

        var use = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => gate.EnsureUseAsync(Tenant).AsTask());
        var bind = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => gate.EnsureBindSecretAsync(Tenant).AsTask());
        var manage = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => gate.EnsureManageAsync(Tenant).AsTask());
        var test = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => gate.EnsureTestAsync(Tenant).AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, use.Code);
        Assert.Equal(ConnectionFaultCodes.Forbidden, bind.Code);
        Assert.Equal(ConnectionFaultCodes.Forbidden, manage.Code);
        Assert.Equal(ConnectionFaultCodes.Forbidden, test.Code);
        Assert.Empty(authorization.Seen);
    }

    [Fact]
    public async Task HttpContextGate_EnforcesUseAndBindSecretForAnAuthenticatedPrincipal()
    {
        var authorization = new FakeAuthorization { Allowed = { ConnectionsPermissions.Use } };
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(IdentityClaimTypes.TenantId, Tenant)],
                    authenticationType: "test"))
            }
        };
        var gate = new HttpContextConnectionPermissionGate(authorization, accessor);

        await gate.EnsureUseAsync(Tenant);

        var refused = await Assert.ThrowsAsync<ConnectionUnavailableException>(() => gate.EnsureBindSecretAsync(Tenant).AsTask());
        Assert.Equal(ConnectionFaultCodes.Forbidden, refused.Code);
        Assert.Equal([ConnectionsPermissions.Use, ConnectionsPermissions.BindSecret], authorization.Seen);
    }

    private static CreateConnectionRequest BearerRequest(string name) => new()
    {
        Name = name,
        AuthScheme = HttpAuthSchemes.Bearer,
        Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
        SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
        {
            [HttpConnectionSecretFields.Token] = new() { Value = "tok" }
        }
    };

    private static ServiceProvider CreateProvider(RecordingGate gate, bool workflows = false) =>
        ConnectionsTestComposition.CreateProvider(workflows: workflows, configure: services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IConnectionPermissionGate>(_ => gate));
            if (workflows)
            {
                services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutionPartitionAccessor>(_ => new FixedPartition(Tenant)));
                services.Replace(ServiceDescriptor.Scoped<IRuntimeSecretMask, DefaultRuntimeSecretMask>());
            }
        });

    private sealed class RecordingGate : IConnectionPermissionGate
    {
        public bool DenyUse { get; init; }
        public bool DenyBindSecret { get; set; }
        public bool DenyManage { get; init; }
        public bool DenyTest { get; init; }
        public List<string> BindCalls { get; } = [];
        public List<string> UseCalls { get; } = [];
        public List<string> TestCalls { get; } = [];

        public ValueTask EnsureManageAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            if (DenyManage)
            {
                throw new ConnectionUnavailableException(
                    "connection",
                    ConnectionFaultCodes.Forbidden,
                    $"Permission '{ConnectionsPermissions.Manage}' is required.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask EnsureUseAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            UseCalls.Add(tenantId);
            if (DenyUse)
            {
                throw new ConnectionUnavailableException(
                    "connection",
                    ConnectionFaultCodes.Forbidden,
                    $"Permission '{ConnectionsPermissions.Use}' is required.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask EnsureTestAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            TestCalls.Add(ConnectionsPermissions.Test);
            if (DenyTest)
            {
                throw new ConnectionUnavailableException(
                    "connection",
                    ConnectionFaultCodes.Forbidden,
                    $"Permission '{ConnectionsPermissions.Test}' is required.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            BindCalls.Add(tenantId);
            if (DenyBindSecret)
            {
                throw new ConnectionUnavailableException(
                    "connection",
                    ConnectionFaultCodes.Forbidden,
                    $"Permission '{ConnectionsPermissions.BindSecret}' is required.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAuthorization : IPermissionAuthorizationService
    {
        public HashSet<string> Allowed { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Seen { get; } = [];

        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            Seen.Add(context.Permission);
            return new(Allowed.Contains(context.Permission)
                ? PermissionEvaluationResult.Success
                : PermissionEvaluationResult.Denied());
        }
    }

    private sealed class FixedPartition(string value) : IWorkflowExecutionPartitionAccessor
    {
        public WorkflowExecutionPartition Current { get; } = new(value);
    }
}
