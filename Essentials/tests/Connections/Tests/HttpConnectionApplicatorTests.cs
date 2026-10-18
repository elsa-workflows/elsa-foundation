using Elsa.Activities.Runtime.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class HttpConnectionApplicatorTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task Apply_AttachesBearerAndRegistersMask()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        var applicator = scope.ServiceProvider.GetRequiredService<Elsa.Activities.Http.Connections.IHttpConnectionApplicator>();
        var mask = (RecordingMask)scope.ServiceProvider.GetRequiredService<IRuntimeSecretMask>();

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "bearer-token",
            Value = "tok-apply",
            TypeName = SecretTypeNames.Text
        });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "acme",
            AuthScheme = HttpAuthSchemes.Bearer,
            Settings = new Dictionary<string, string> { [HttpConnectionSettingKeys.AllowedHosts] = "api.example" },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.Token] = new() { ExistingSecretName = "bearer-token" }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/v1");
        var context = new ActivityExecutionContext("wf", "actexec-1", "attempt-1", "node-1", CancellationToken.None);
        await applicator.ApplyAsync(request, new Elsa.Activities.Runtime.Core.Models.ConnectionReference("acme", "http"), context);

        Assert.Equal("Bearer tok-apply", request.Headers.GetValues("Authorization").Single());
        Assert.True(mask.HasRegistrations("actexec-1"));
        Assert.Equal("[secret:acme:token]", mask.Mask("actexec-1", "tok-apply"));
    }

    [Fact]
    public void OAuthCallback_IsTheReservedBackendPath()
    {
        Assert.Equal("/_elsa/connections/oauth/callback", Elsa.Connections.Api.ConnectionsApi.OAuthCallbackPath);
    }

    private static ServiceProvider CreateProvider() =>
        ConnectionsTestComposition.CreateProvider(workflows: true, configure: services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutionPartitionAccessor>(_ => new FixedPartition(Tenant)));
            services.Replace(ServiceDescriptor.Scoped<IRuntimeSecretMask, RecordingMask>());
        });

    private sealed class FixedPartition(string value) : IWorkflowExecutionPartitionAccessor
    {
        public WorkflowExecutionPartition Current { get; } = new(value);
    }

    private sealed class RecordingMask : IRuntimeSecretMask
    {
        private readonly DefaultRuntimeSecretMask _inner = new();

        public void Register(string activityExecutionId, string referenceName, string value) =>
            _inner.Register(activityExecutionId, referenceName, value);

        public bool HasRegistrations(string activityExecutionId) => _inner.HasRegistrations(activityExecutionId);

        public string Mask(string activityExecutionId, string text) => _inner.Mask(activityExecutionId, text);

        public void Release(string activityExecutionId) => _inner.Release(activityExecutionId);
    }
}
