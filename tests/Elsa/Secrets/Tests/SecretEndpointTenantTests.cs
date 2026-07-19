using System.Security.Claims;
using Elsa.Primitives.Persistence;
using Elsa.Secrets.Api.Features;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretEndpointTenantTests
{
    [Fact]
    public async Task Every_secret_data_endpoint_forwards_only_the_authenticated_tenant()
    {
        foreach (var endpointCase in EndpointCases())
        {
            var manager = new RecordingSecretManager();
            var endpoint = CreateEndpoint(endpointCase.EndpointName, manager, Principal("tenant-1"));

            await HandleAsync(endpoint, endpointCase.Request);

            Assert.Equal("tenant-1", Assert.Single(manager.Tenants));
        }
    }

    [Fact]
    public async Task Every_secret_data_endpoint_fails_closed_when_the_tenant_claim_is_missing()
    {
        foreach (var endpointCase in EndpointCases())
        {
            var manager = new RecordingSecretManager();
            var endpoint = CreateEndpoint(endpointCase.EndpointName, manager, Principal(tenantId: null));

            await HandleAsync(endpoint, endpointCase.Request);

            Assert.Equal(StatusCodes.Status403Forbidden, endpoint.HttpContext.Response.StatusCode);
            Assert.Empty(manager.Tenants);
        }
    }

    private static IReadOnlyCollection<EndpointCase> EndpointCases() =>
    [
        new("Create", new CreateSecretRequest { Name = "payments.api", Value = "value" }),
        new("Get", new GetSecretRequest { Name = "payments.api" }),
        new("List", new SecretQuery()),
        new("Picker", new SecretPickerRequest()),
        new("Update", new UpdateSecretApiRequest { Name = "payments.api" }),
        new("Rotate", new RotateSecretApiRequest { Name = "payments.api", Value = "value" }),
        new("Revoke", new RevokeSecretRequest { Name = "payments.api" }),
        new("Delete", new DeleteSecretRequest { Name = "payments.api" }),
        new("Test", new TestSecretRequest { Name = "payments.api" })
    ];

    private static ClaimsPrincipal Principal(string? tenantId)
    {
        var claims = tenantId is null ? [] : new[] { new Claim("elsa.identity.tenant_id", tenantId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static BaseEndpoint CreateEndpoint(string endpointName, ISecretManager manager, ClaimsPrincipal principal)
    {
        var endpointType = typeof(SecretsApiFeature).Assembly.GetType(
            $"Elsa.Secrets.Api.Endpoints.Secrets.{endpointName}",
            throwOnError: true)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        Action<DefaultHttpContext> configure = context =>
        {
            context.User = principal;
            context.Response.Body = new MemoryStream();
        };
        return (BaseEndpoint)create.Invoke(null, [configure, new object[] { manager }])!;
    }

    private static Task HandleAsync(BaseEndpoint endpoint, object request) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [request.GetType(), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private sealed record EndpointCase(string EndpointName, object Request);

    private sealed class RecordingSecretManager : ISecretManager
    {
        public List<string> Tenants { get; } = [];

        public ValueTask<SecretMetadata> CreateAsync(string tenantId, CreateSecretRequest request, CancellationToken cancellationToken = default) =>
            Result(tenantId);

        public ValueTask<SecretMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenantId);
            return ValueTask.FromResult<SecretMetadata?>(Metadata(tenantId));
        }

        public ValueTask<Page<SecretMetadata>> ListAsync(string tenantId, SecretQuery query, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenantId);
            return ValueTask.FromResult(Page.Of<SecretMetadata>([Metadata(tenantId)], 1));
        }

        public ValueTask<SecretMetadata> UpdateAsync(string tenantId, string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default) =>
            Result(tenantId);

        public ValueTask<SecretMetadata> RotateAsync(string tenantId, string name, RotateSecretRequest request, CancellationToken cancellationToken = default) =>
            Result(tenantId);

        public ValueTask<SecretMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenantId);
            return ValueTask.FromResult<SecretMetadata?>(Metadata(tenantId));
        }

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenantId);
            return ValueTask.FromResult(true);
        }

        public ValueTask<SecretTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default)
        {
            Tenants.Add(tenantId);
            return ValueTask.FromResult(SecretTestResult.Success());
        }

        private ValueTask<SecretMetadata> Result(string tenantId)
        {
            Tenants.Add(tenantId);
            return ValueTask.FromResult(Metadata(tenantId));
        }

        private static SecretMetadata Metadata(string tenantId) => new()
        {
            TenantId = tenantId,
            Name = "payments.api",
            DisplayName = "Payments API"
        };
    }
}
