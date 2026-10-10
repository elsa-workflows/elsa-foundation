using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CShells.AspNetCore.Features;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Activities.Design.Api;
using Elsa.Api.AspNetCore;
using Elsa.Modularity.Api;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;
using Elsa3.Activities.Design.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NativeEndpoints;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Spec 180, FR-016a: every domain API that can raise a schema write refusal answers it with HTTP 409 in its own problem
/// envelope, carrying the refusal's code, never a 500, a 400 or a success. An API sees no EF Core, so it answers the
/// <see cref="SchemaWriteRefusedException"/> both EF refusals derive from.
/// </summary>
/// <remarks>
/// Three mechanisms make it hold. An owner with no failure services of its own answers through the unkeyed translator
/// <c>AddElsaEndpoints</c> registers and its problem writer; an owner that renders its own shapes answers in each of them,
/// checked for every endpoint it maps; and an operation outside the failure pipeline is answered by
/// <see cref="ElsaEndpointGroupExtensions.MapUnboundOperation"/>.
/// </remarks>
public sealed class SchemaWriteRefusalMappingGuardTests
{
    private const string ProbeFamily = "Probe.Family";
    private const string ProbeWriteVersion = "7.0.0";
    private const string ProbeRequiredVersion = "8.0.0";
    private const string ProbeFeature = "Probe.DormantFeature";
    // No character a JSON writer escapes, so the body carries it verbatim whichever envelope an owner writes.
    private const string ProbeReason = "It becomes available once every host can read the newer version of Probe.Family.";

    /// <summary>Every first-party feature that registers failure services for its owner.</summary>
    private static readonly Type[] OwnersWithTheirOwnFailureServices =
    [
        typeof(ActivitiesBpmnInterchangeFeature),
        typeof(ActivitiesDesignApiFeature),
        typeof(ModularityApiFeature),
        typeof(WorkflowsDesignApiFeature),
        typeof(WorkflowsPublishingApiFeature),
        typeof(WorkflowsRuntimeApiFeature),
        typeof(Elsa3ImportActivitiesFeature)
    ];

    public static TheoryData<Type> Owners => [.. OwnersWithTheirOwnFailureServices];

    /// <summary>
    /// Each endpoint's failure runs the way the pipeline runs it (Elsa.Api.AspNetCore's EXTENSION_POINTS.md, "Failure
    /// pipeline contracts"), with the endpoint's own metadata, so every shape an owner scopes by endpoint is reached.
    /// </summary>
    [Theory]
    [MemberData(nameof(Owners))]
    public async Task Every_endpoint_of_an_owner_with_its_own_failure_shapes_answers_a_refusal_with_409_and_its_code(Type feature)
    {
        var (app, endpoints) = Map(feature);
        var wrong = new List<string>();
        await using (app)
        {
            foreach (var endpoint in endpoints)
            {
                var (status, body) = await FailAsync(app.Services, endpoint, new ProbeRefusal());
                if (status != StatusCodes.Status409Conflict || !CarriesTheRefusal(body))
                    wrong.Add($"{endpoint.DisplayName} ({endpoint.RoutePattern.RawText}) answered {status}: {body}");
            }
        }

        Assert.True(wrong.Count == 0, $"{feature.Name}: {wrong.Count} of {endpoints.Length} endpoints do not answer a schema write refusal with 409 and its code:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    /// <summary>
    /// Spec 182, FR-015: the dormancy refusal the shared dormancy check raises is answered the same way by every endpoint,
    /// with its code, and with the feature and the reason it adds, which its message carries into every shape.
    /// </summary>
    [Theory]
    [MemberData(nameof(Owners))]
    public async Task Every_endpoint_of_an_owner_with_its_own_failure_shapes_answers_a_dormancy_refusal_with_409_its_code_feature_and_reason(Type feature)
    {
        var (app, endpoints) = Map(feature);
        var wrong = new List<string>();
        await using (app)
        {
            foreach (var endpoint in endpoints)
            {
                var (status, body) = await FailAsync(app.Services, endpoint, DormancyProbe());
                if (status != StatusCodes.Status409Conflict || !CarriesFamilyAndVersions(body) || !CarriesTheDormancy(body))
                    wrong.Add($"{endpoint.DisplayName} ({endpoint.RoutePattern.RawText}) answered {status}: {body}");
            }
        }

        Assert.True(wrong.Count == 0, $"{feature.Name}: {wrong.Count} of {endpoints.Length} endpoints do not answer a dormancy refusal with 409, its own code (\"schema-version-not-finalized\"), feature and reason:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    /// <summary>
    /// Publishing's shapes each have an <c>errorCode</c> member (#1699), so the refusal's code goes there, and not only
    /// among the errors a translated problem would give it.
    /// </summary>
    [Fact]
    public async Task Publishing_puts_the_refusal_code_in_the_error_code_of_every_endpoint()
    {
        var (app, endpoints) = Map(typeof(WorkflowsPublishingApiFeature));
        await using (app)
        {
            foreach (var endpoint in endpoints)
            {
                var (_, body) = await FailAsync(app.Services, endpoint, new ProbeRefusal());
                Assert.True(
                    JsonDocument.Parse(body).RootElement.TryGetProperty("errorCode", out var errorCode) &&
                    errorCode.GetString() == SchemaWriteRefusedException.RefusalCode,
                    $"{endpoint.DisplayName} answered without the refusal's errorCode: {body}");
            }
        }
    }

    /// <summary>
    /// Through the real pipeline, for an owner with no failure services: a contained operation answers through the shared
    /// translator and the fallback writer, and an operation outside the pipeline through the answer MapUnboundOperation
    /// wraps it in. Neither swallows any other failure.
    /// </summary>
    [Fact]
    public async Task An_owner_without_failure_services_answers_a_refusal_in_and_outside_the_pipeline()
    {
        await using var host = await RefusalProbeHost.StartAsync();

        var (containedStatus, contained) = await host.GetAsync("/contained/refusal");
        Assert.Equal(HttpStatusCode.Conflict, containedStatus);
        Assert.Equal(409, contained.GetProperty("status").GetInt32());
        var errors = contained.GetProperty("errors");
        Assert.Equal(SchemaWriteRefusedException.RefusalCode, errors.GetProperty("code")[0].GetString());
        Assert.Equal(ProbeFamily, errors.GetProperty("family")[0].GetString());
        Assert.Equal(ProbeWriteVersion, errors.GetProperty("writeVersion")[0].GetString());
        Assert.Equal(ProbeRequiredVersion, errors.GetProperty("requiredVersion")[0].GetString());
        Assert.Equal(new ProbeRefusal().Message, errors.GetProperty("generalErrors")[0].GetString());

        var (uncontainedStatus, uncontained) = await host.GetAsync("/uncontained/refusal");
        Assert.Equal(HttpStatusCode.Conflict, uncontainedStatus);
        Assert.Equal(409, uncontained.GetProperty("status").GetInt32());
        Assert.Equal("Conflict", uncontained.GetProperty("title").GetString());
        Assert.Equal(new ProbeRefusal().Message, uncontained.GetProperty("detail").GetString());
        Assert.Equal("/uncontained/refusal", uncontained.GetProperty("instance").GetString());
        var pairs = uncontained.GetProperty("errors").EnumerateArray()
            .Select(error => (error.GetProperty("name").GetString(), error.GetProperty("reason").GetString()))
            .ToArray();
        Assert.Contains(("code", SchemaWriteRefusedException.RefusalCode), pairs);
        Assert.Contains(("family", ProbeFamily), pairs);
        Assert.Contains(("writeVersion", ProbeWriteVersion), pairs);
        Assert.Contains(("requiredVersion", ProbeRequiredVersion), pairs);

        // A dormancy refusal carries its own code (spec 182, Q17), never the store-level one, plus its feature and its
        // caller-neutral reason as entries of their own (FR-013).
        var (dormantStatus, dormant) = await host.GetAsync("/contained/dormant");
        Assert.Equal(HttpStatusCode.Conflict, dormantStatus);
        var dormantErrors = dormant.GetProperty("errors");
        Assert.Equal("schema-version-not-finalized", dormantErrors.GetProperty("code")[0].GetString());
        Assert.Equal(ProbeFeature, dormantErrors.GetProperty("feature")[0].GetString());
        Assert.Equal(ProbeReason, dormantErrors.GetProperty("reason")[0].GetString());
        Assert.False(errors.TryGetProperty("feature", out _), "A store's refusal names no feature.");
        Assert.False(errors.TryGetProperty("reason", out _), "A store's refusal carries no dormancy reason.");

        // Any other failure keeps its way out: the pipeline's sanitized 500, and the host's own handling outside it.
        var (otherContainedStatus, _) = await host.GetAsync("/contained/other");
        Assert.Equal(HttpStatusCode.InternalServerError, otherContainedStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.GetAsync("/uncontained/other"));

        // A refusal after the response started cannot be answered, and is not hidden in a truncated success either.
        await Assert.ThrowsAnyAsync<Exception>(() => host.GetAsync("/uncontained/after-start"));
    }

    /// <summary>The owner <paramref name="feature"/> composes, alone, with every endpoint it maps.</summary>
    private static (WebApplication App, RouteEndpoint[] Endpoints) Map(Type feature)
    {
        var builder = WebApplication.CreateSlimBuilder();
        var shellFeature = (IWebShellFeature)Activator.CreateInstance(feature)!;
        shellFeature.ConfigureServices(builder.Services);
        var app = builder.Build();
        shellFeature.MapEndpoints(app, null);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.NotEmpty(endpoints);
        return (app, endpoints);
    }

    /// <summary>Proves the dormancy refusal answers with its own code (spec 182, Q17), never the store-level one.</summary>
    private static bool CarriesTheDormancy(string body) =>
        body.Contains("schema-version-not-finalized", StringComparison.Ordinal) &&
        body.Contains(ProbeFeature, StringComparison.Ordinal) &&
        body.Contains(ProbeReason, StringComparison.Ordinal);

    private static bool CarriesTheRefusal(string body) =>
        body.Contains("schema-write-refused", StringComparison.Ordinal) && CarriesFamilyAndVersions(body);

    private static bool CarriesFamilyAndVersions(string body) =>
        body.Contains(ProbeFamily, StringComparison.Ordinal) &&
        body.Contains(ProbeWriteVersion, StringComparison.Ordinal) &&
        body.Contains(ProbeRequiredVersion, StringComparison.Ordinal);

    /// <summary>
    /// The pipeline's failure path: the owner's fault renderers, then the unkeyed ones, the first that writes owning the
    /// response; then the owner's translators, then the unkeyed ones, the first problem winning, else the sanitized 500;
    /// written by the owner's problem writer, else the unkeyed one.
    /// </summary>
    private static async Task<(int Status, string Body)> FailAsync(IServiceProvider services, RouteEndpoint endpoint, Exception exception)
    {
        var owner = Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()).OwnerId;
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, Response = { Body = new MemoryStream() } };
        context.Request.Path = "/refusal-probe";
        context.SetEndpoint(endpoint);

        var written = false;
        foreach (var renderer in scope.ServiceProvider.GetKeyedServices<IEndpointFaultRenderer>(owner).Concat(scope.ServiceProvider.GetServices<IEndpointFaultRenderer>()))
        {
            written = await renderer.TryWriteAsync(context, exception);
            if (written)
                break;
        }

        if (!written)
        {
            var problem = scope.ServiceProvider.GetKeyedServices<IEndpointExceptionTranslator>(owner)
                              .Concat(scope.ServiceProvider.GetServices<IEndpointExceptionTranslator>())
                              .Select(translator => translator.Translate(exception))
                              .FirstOrDefault(candidate => candidate is not null)
                          ?? EndpointProblem.General(StatusCodes.Status500InternalServerError, "Unexpected error occurred");
            var writer = scope.ServiceProvider.GetKeyedService<IEndpointProblemWriter>(owner) ?? scope.ServiceProvider.GetRequiredService<IEndpointProblemWriter>();
            await writer.WriteAsync(context, problem);
        }

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    /// <summary>A dormancy refusal as the shared dormancy check raises one (spec 182, FR-013).</summary>
    private static SchemaDormancyRefusedException DormancyProbe() =>
        new(ProbeFamily, ProbeWriteVersion, ProbeRequiredVersion, ProbeFeature, ProbeReason);

    /// <summary>A refusal as a store raises one, without the EF Core an API never resolves.</summary>
    private sealed class ProbeRefusal() : SchemaWriteRefusedException(
        ProbeFamily, ProbeWriteVersion, ProbeRequiredVersion,
        $"Schema family '{ProbeFamily}' refused a write: the value needs schema version '{ProbeRequiredVersion}', but this host may write only '{ProbeWriteVersion}'.");

    /// <summary>One owner, mapped through the real pipeline, with no failure services of its own.</summary>
    private sealed class RefusalProbeHost(WebApplication app) : IAsyncDisposable
    {
        private readonly HttpClient _client = app.GetTestClient();

        public static async Task<RefusalProbeHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting().AddElsaEndpoints();
            var app = builder.Build();
            var api = app.MapEndpointGroup("Tests.SchemaWriteRefusalProbe", RefusalProbeJsonContext.Default);
            Map(api, "/contained/refusal", () => throw new ProbeRefusal(), contain: true);
            Map(api, "/contained/dormant", () => throw DormancyProbe(), contain: true);
            Map(api, "/contained/other", () => throw new InvalidOperationException("Not a refusal."), contain: true);
            Map(api, "/uncontained/refusal", () => throw new ProbeRefusal(), contain: false);
            Map(api, "/uncontained/other", () => throw new InvalidOperationException("Not a refusal."), contain: false);
            api.MapUnboundOperation("GET", "/uncontained/after-start", "AfterStart", null, StatusCodes.Status200OK, null, async context =>
            {
                await context.Response.StartAsync();
                throw new ProbeRefusal();
            }, containFailures: false);
            await app.StartAsync();
            return new RefusalProbeHost(app);
        }

        public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
        {
            using var response = await _client.GetAsync(path);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await app.DisposeAsync();
        }

        private static void Map(EndpointGroup api, string route, Action fail, bool contain) =>
            api.MapUnboundOperation("GET", route, route.Replace("/", string.Empty, StringComparison.Ordinal), null, StatusCodes.Status200OK, null, _ =>
            {
                fail();
                return Task.CompletedTask;
            }, containFailures: contain);
    }
}

/// <summary>The probe owner's JSON context; its operations answer no body of their own.</summary>
[JsonSerializable(typeof(string))]
internal sealed partial class RefusalProbeJsonContext : JsonSerializerContext;
