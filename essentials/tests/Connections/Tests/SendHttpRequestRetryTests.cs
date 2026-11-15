using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Runtime.Core.Contracts;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class SendHttpRequestRetryTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task ApiKey_connection_getting_401_sends_exactly_one_request()
    {
        var handler = new CountingUnauthorizedHandler();
        await using var provider = ConnectionsTestComposition.CreateProvider(workflows: true, configure: services =>
        {
            services.AddOptions<HttpActivityOptions>();
            services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutionPartitionAccessor>(_ => new FixedPartition(Tenant)));
            services.TryAddScoped<IRuntimeSecretMask, DefaultRuntimeSecretMask>();
            services.AddHttpClient(HttpActivityConstants.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        await using var scope = provider.CreateAsyncScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();

        await secrets.CreateAsync(Tenant, new CreateSecretRequest
        {
            Name = "api-key-value",
            Value = "key-401",
            TypeName = SecretTypeNames.Text
        });
        await manager.CreateAsync(Tenant, new CreateConnectionRequest
        {
            Name = "api-key-conn",
            AuthScheme = HttpAuthSchemes.ApiKey,
            Settings = new Dictionary<string, string>
            {
                [HttpConnectionSettingKeys.AllowedHosts] = "api.example"
            },
            SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
            {
                [HttpConnectionSecretFields.ApiKey] = new() { ExistingSecretName = "api-key-value" }
            }
        });

        var activity = new SendHttpRequest(
            scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(),
            scope.ServiceProvider.GetRequiredService<IOptions<HttpActivityOptions>>(),
            scope.ServiceProvider.GetRequiredService<IHttpConnectionApplicator>())
        {
            Url = new Uri("https://api.example/resource"),
            Method = "GET",
            Connection = new ConnectionReference("api-key-conn", ConnectionTypeNames.Http)
        };
        var context = new ActivityExecutionContext("wf", "actexec-apikey", "attempt-1", "node-1", CancellationToken.None);
        var transition = await ((IActivity)activity).ExecuteAsync(context);
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<SendHttpRequestResult>>(transition);

        Assert.Equal(401, completion.Result.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class CountingUnauthorizedHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        }
    }

    private sealed class FixedPartition(string value) : IWorkflowExecutionPartitionAccessor
    {
        public WorkflowExecutionPartition Current { get; } = new(value);
    }
}
