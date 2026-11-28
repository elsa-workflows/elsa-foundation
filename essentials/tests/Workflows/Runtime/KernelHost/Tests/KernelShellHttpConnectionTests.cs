using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    [Theory]
    [InlineData(HttpAuthSchemes.ApiKey, HttpConnectionSecretFields.ApiKey, false, false)]
    [InlineData(HttpAuthSchemes.Basic, HttpConnectionSecretFields.Password, false, false)]
    [InlineData(HttpAuthSchemes.Bearer, HttpConnectionSecretFields.Token, false, false)]
    [InlineData(HttpAuthSchemes.ApiKey, HttpConnectionSecretFields.ApiKey, true, false)]
    [InlineData(HttpAuthSchemes.ApiKey, HttpConnectionSecretFields.ApiKey, false, true)]
    public async Task Sqlite_actor_sends_bound_credentials_and_masks_the_retained_response(string scheme, string secretField,
        bool duplicateProperties, bool replaceIdempotencyKey)
    {
        const string duplicateJson = """{"ordinary":"first","ordinary":"second"}""";
        var credential = scheme == HttpAuthSchemes.Bearer ? "fixture-token+prefixed" : "fixture-token+ö";
        const string username = "fixture-user";
        const string authoredKey = "fixture-partner-idempotency-key";
        var selectsKey = scheme == HttpAuthSchemes.Basic || replaceIdempotencyKey;
        var stored = scheme == HttpAuthSchemes.Bearer ? "Bearer " + credential : credential;
        var echoed = scheme == HttpAuthSchemes.Basic ? Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + credential)) : credential;
        const string workflowPath = "actor-bound-send";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var calls = new ConcurrentQueue<(string Credential, string Authorization, string Cookie, string IdempotencyKey)>();
        var redirects = 0;
        var receiverBuilder = WebApplication.CreateSlimBuilder();
        receiverBuilder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        receiverBuilder.Logging.ClearProviders();
        await using var receiver = receiverBuilder.Build();
        receiver.MapGet("/original", (HttpContext context) =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            var key = scheme == HttpAuthSchemes.ApiKey
                ? context.Request.Query["key"].ToString()
                : System.Net.Http.Headers.AuthenticationHeaderValue.Parse(authorization).Parameter!;
            calls.Enqueue((key, authorization, context.Request.Headers.Cookie.ToString(),
                context.Request.Headers[SendHttpRequest.IdempotencyKeyHeader].ToString()));
            context.Response.Headers.Location = "/redirected?echo=" + Uri.EscapeDataString(key);
            context.Response.Headers.SetCookie = "never-replay=fixture; Path=/";
            context.Response.Headers["x-echo"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
            context.Response.StatusCode = 302;
            if (duplicateProperties)
            {
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync(duplicateJson, context.RequestAborted);
            }

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
            var runtime = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory.FullName, "runtime.db"), Pooling = false
            }.ToString();
            KernelRuntimeFixture.Add(services, "Sqlite", runtime);
            new WorkflowsRuntimeKernelHostingFeature { Provider = "Sqlite", SqlitePath = Path.Combine(directory.FullName, "foundation.db") }
                .ConfigureServices(services);
            new WorkflowsRuntimeKernelFeature().ConfigureServices(services);
            new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
            await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using (var setup = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(tenant))
            {
                await setup.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
                var settings = new Dictionary<string, string>
                {
                    [HttpConnectionSettingKeys.AllowedHosts] = IPAddress.Loopback.ToString(),
                    [HttpConnectionSettingKeys.AllowInsecureHttp] = "true"
                };
                if (replaceIdempotencyKey)
                {
                    settings[HttpConnectionSettingKeys.ApiKeyPlacement] = ApiKeyPlacements.Header;
                    settings[HttpConnectionSettingKeys.ApiKeyHeaderName] = SendHttpRequest.IdempotencyKeyHeader;
                }
                else if (scheme == HttpAuthSchemes.ApiKey)
                {
                    settings[HttpConnectionSettingKeys.ApiKeyPlacement] = ApiKeyPlacements.Query;
                    settings[HttpConnectionSettingKeys.ApiKeyQueryName] = "key";
                }
                if (scheme == HttpAuthSchemes.Basic)
                    settings[HttpConnectionSettingKeys.Username] = username;
                await setup.ServiceProvider.GetRequiredService<IConnectionManager>().CreateAsync(tenant.Value, new()
                {
                    Name = "loopback-partner",
                    AuthScheme = scheme,
                    Settings = settings,
                    SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                    {
                        [secretField] = new() { Value = stored }
                    }
                }, token);
            }

            var shell = host.GetRequiredService<KernelShellRuntime>();
            await shell.StartAsync(token);
            try
            {
                var serving = host.GetRequiredService<KernelServing>();
                var address = receiver.Urls.Single() + "/original";
                var send = Wf.Node<SendHttpRequest>("send", ("Url", address), ("Method", "GET"), ("Connection", "loopback-partner"));
                if (selectsKey)
                    send.Inputs["Headers"] = Binding.Literal(new JsonObject { [SendHttpRequest.IdempotencyKeyHeader] = authoredKey });
                await serving.ServeHostWorkflowAsync(Wf.Workflow("bound-send", Wf.Flowchart("steps",
                    [Wf.HttpEndpoint("start", workflowPath), send,
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
                    if (replaceIdempotencyKey)
                    {
                        Assert.Equal(500, context.Response.StatusCode);
                        Assert.Empty(calls);
                        return;
                    }

                    Assert.Equal(200, context.Response.StatusCode);
                    using var result = JsonDocument.Parse(response.ToArray());
                    var data = result.RootElement;
                    Assert.Equal(302, data.GetProperty("StatusCode").GetInt32());
                    var body = data.GetProperty("Body");
                    if (duplicateProperties)
                        Assert.Equal(duplicateJson, body.GetString());
                    else
                    {
                        Assert.False(body.TryGetProperty(echoed, out _));
                        Assert.Contains("[secret:", body.GetProperty("visible")[0].GetString());
                        Assert.Equal("ordinary value", body.GetProperty("visible")[1].GetString());
                    }
                    var headers = data.GetProperty("Headers");
                    Assert.Contains("[secret:", headers.GetProperty("location").GetString());
                    Assert.Contains("[secret:", headers.GetProperty("x-echo").GetString());
                    Assert.False(headers.TryGetProperty("set-cookie", out _));

                    var actor = new Kernel.Foundation.Contracts.ActorRef(Assert.Single(context.Response.Headers[KernelHttpEdge.WorkflowInstanceIdHeader])!);
                    var reads = await shell.ReadAsync(token);
                    var journal = await reads.TailAsync(scope, actor, cancellationToken: token);
                    var persisted = JsonSerializer.Serialize(journal);
                    Assert.DoesNotContain("fixture-token", persisted);
                    Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(echoed)), persisted);
                }

                var expectedHeader = scheme switch
                {
                    HttpAuthSchemes.Basic => "Basic " + echoed,
                    HttpAuthSchemes.Bearer => "Bearer " + echoed,
                    _ => string.Empty
                };
                Assert.Equal(2, calls.Count);
                Assert.All(calls, call =>
                {
                    Assert.Equal((echoed, expectedHeader, ""), (call.Credential, call.Authorization, call.Cookie));
                    if (selectsKey) Assert.Equal(authoredKey, call.IdempotencyKey);
                    else Assert.NotEmpty(call.IdempotencyKey);
                });
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
