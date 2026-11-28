using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Extensions;
using Elsa.Connections.Workflows.Features;
using Elsa.Secrets.Extensions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelShellHttpConnectionTests
{
    [Fact]
    public async Task Sqlite_actor_sends_bound_query_credentials_and_masks_the_retained_response()
    {
        const string credential = "fixture-token+ö";
        const string workflowPath = "actor-bound-send";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var calls = new ConcurrentQueue<(string? Key, string Cookie)>();
        var redirects = 0;
        var receiverBuilder = WebApplication.CreateSlimBuilder();
        receiverBuilder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        receiverBuilder.Logging.ClearProviders();
        await using var receiver = receiverBuilder.Build();
        receiver.MapGet("/original", (HttpContext context) =>
        {
            var key = context.Request.Query["key"].ToString();
            calls.Enqueue((key, context.Request.Headers.Cookie.ToString()));
            context.Response.Headers.Location = "/redirected?echo=" + Uri.EscapeDataString(key);
            context.Response.Headers.SetCookie = "never-replay=fixture; Path=/";
            context.Response.Headers["x-echo"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
            context.Response.StatusCode = 302;
            return context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
            {
                ["visible"] = new object?[] { key, "ordinary value" },
                [key] = "field name contains the credential"
            }, context.RequestAborted);
        });
        receiver.MapGet("/redirected", () =>
        {
            Interlocked.Increment(ref redirects);
            return Results.NoContent();
        });
        await receiver.StartAsync(token);

        var directory = Directory.CreateTempSubdirectory("kernel-actor-http-connection");
        try
        {
            var tenant = new PersistenceScope(PersistenceScope.DefaultValue);
            var scope = EmbeddedTenant.Bind(tenant.Value);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "kernel-http-connection-testing-key"
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddSecrets(config);
            services.AddConnections(config);
            new ActorHttpConnectionsFeature().ConfigureServices(services);
            services.AddWorkflowRuntime();
            services.AddRuntimeEntityFrameworkCore(new()
            {
                Provider = "Sqlite", ConnectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory.FullName, "runtime.db"), Pooling = false
                }.ToString(),
                RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
            });
            new WorkflowsRuntimeKernelHostingFeature { Provider = "Sqlite", SqlitePath = Path.Combine(directory.FullName, "foundation.db") }
                .ConfigureServices(services);
            services.AddPersistenceModes();
            new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
            await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using (var setup = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(tenant))
            {
                await setup.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
                await setup.ServiceProvider.GetRequiredService<IConnectionManager>().CreateAsync(tenant.Value, new()
                {
                    Name = "loopback-partner",
                    AuthScheme = HttpAuthSchemes.ApiKey,
                    Settings = new Dictionary<string, string>
                    {
                        [HttpConnectionSettingKeys.AllowedHosts] = IPAddress.Loopback.ToString(),
                        [HttpConnectionSettingKeys.AllowInsecureHttp] = "true",
                        [HttpConnectionSettingKeys.ApiKeyPlacement] = ApiKeyPlacements.Query,
                        [HttpConnectionSettingKeys.ApiKeyQueryName] = "key"
                    },
                    SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                    {
                        [HttpConnectionSecretFields.ApiKey] = new() { Value = credential }
                    }
                }, token);
            }

            var shell = host.GetRequiredService<KernelShellRuntime>();
            await shell.StartAsync(token);
            try
            {
                var serving = host.GetRequiredService<KernelServing>();
                var address = receiver.Urls.Single() + "/original";
                await serving.ServeHostWorkflowAsync(Wf.Workflow("bound-send", Wf.Flowchart("steps",
                    [Wf.HttpEndpoint("start", workflowPath),
                        Wf.Node<SendHttpRequest>("send", ("Url", address), ("Method", "GET"), ("Connection", "loopback-partner")),
                        Wf.Respond("answer", Binding.Result("send"), contentType: "application/json")],
                    [new Connection("start", "send", "Done"), new Connection("send", "answer", SendHttpRequest.FailedOutcome)])), token);
                var handler = new KernelHttpRequestHandler(serving, shell.IngressAsync,
                    host.GetRequiredService<KernelShellStorage>().ReadClockAsync, scope, "/workflows/http",
                    TimeSpan.FromSeconds(10), maxRequestBodyBytes: 4096, enforceRequestTimeout: true);

                for (var run = 0; run < 2; run++)
                {
                    await using var response = new MemoryStream();
                    var context = new DefaultHttpContext { RequestAborted = token };
                    var path = "/workflows/http/" + workflowPath;
                    context.Request.Path = path;
                    context.Request.Method = "POST";
                    context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget = path;
                    context.Response.Body = response;
                    await handler.HandleAsync(context);
                    Assert.Equal(200, context.Response.StatusCode);
                    using var result = JsonDocument.Parse(response.ToArray());
                    var data = result.RootElement;
                    Assert.Equal(302, data.GetProperty("StatusCode").GetInt32());
                    var body = data.GetProperty("Body");
                    Assert.False(body.TryGetProperty(credential, out _));
                    Assert.Contains("[secret:", body.GetProperty("visible")[0].GetString());
                    Assert.Equal("ordinary value", body.GetProperty("visible")[1].GetString());
                    var headers = data.GetProperty("Headers");
                    Assert.Contains("[secret:", headers.GetProperty("location").GetString());
                    Assert.Contains("[secret:", headers.GetProperty("x-echo").GetString());
                    Assert.False(headers.TryGetProperty("set-cookie", out _));

                    var actor = new Kernel.Foundation.Contracts.ActorRef(Assert.Single(context.Response.Headers[KernelHttpEdge.WorkflowInstanceIdHeader])!);
                    var reads = await shell.ReadAsync(token);
                    var journal = await reads.TailAsync(scope, actor, cancellationToken: token);
                    Assert.DoesNotContain("fixture-token", JsonSerializer.Serialize(journal));
                }

                Assert.Equal(new[] { (credential, ""), (credential, "") }, calls.ToArray());
                Assert.Equal(0, Volatile.Read(ref redirects));
            }
            finally
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await shell.StopAsync(stop.Token);
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
