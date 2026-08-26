using Elsa.Api.AspNetCore;
using Elsa.Api.Endpoints.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Api.Endpoints.Tests;

/// <summary>
/// Request-time coverage for the <see cref="ModuleEndpointGroup"/> pipeline through a real
/// TestServer: shape execution, binder-problem writing, the renderer → translator → sanitized-500
/// failure ladder, owner-keyed service resolution, cancellation, and the success content type.
/// </summary>
public sealed class ModuleEndpointPipelineTests
{
    private static StringContent Json(string payload) => new(payload, Encoding.UTF8, "application/json");

    // ---------- Shapes end to end ----------

    [Fact]
    public async Task Body_shape_binds_route_body_and_query_and_writes_json()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.BodyShape>());

        var response = await host.Client.PostAsync("/items/route-1?note=q-note", Json("""{"name":"n","count":7}"""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("""{"value":"route-1|n|7|q-note"}""", body);
    }

    [Fact]
    public async Task No_content_shape_writes_an_empty_204()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.NoContentShape>());

        var response = await host.Client.DeleteAsync("/items/route-9");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unbound_shape_serves_without_a_request_contract()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.UnboundShape>());

        var response = await host.Client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"value":"unbound"}""", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("""{"name":"reviewed"}""", HttpStatusCode.Created)]
    [InlineData("""{}""", HttpStatusCode.Accepted)]
    public async Task Result_shape_writes_the_status_the_handler_chose(string payload, HttpStatusCode expected)
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.ResultShape>());

        var response = await host.Client.PostAsync("/items/r-1/outcomes", Json(payload));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("""{"value":"r-1"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_success_content_type_is_the_module_configured_one()
    {
        await using var host = await PipelineHost.StartAsync(
            api => api.MapEndpoint<ShapeEndpoints.UnboundShape>(),
            jsonContentType: "application/json; charset=utf-8");

        var response = await host.Client.GetAsync("/status");

        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    // ---------- Binder problems ----------

    [Fact]
    public async Task A_malformed_body_is_a_400_with_the_serializer_errors_key()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.BodyShape>());

        var response = await host.Client.PostAsync("/items/x", Json("{"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("serializerErrors", body);
        Assert.Contains("\"writer\":\"unkeyed\"", body);
    }

    [Fact]
    public async Task A_literal_null_body_is_a_400_with_the_serializer_errors_key()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.BodyShape>());

        var response = await host.Client.PostAsync("/items/x", Json("null"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("serializerErrors", body);
        Assert.Contains("A request body is required.", body);
    }

    [Fact]
    public async Task Declared_accepts_metadata_rejects_a_wrong_content_type_with_a_bare_415_at_routing()
    {
        // WithModuleOperation always declares accepts for request-carrying operations (defaulting
        // to application/json), so ASP.NET Core's matcher policy short-circuits a wrong content
        // type before the pipeline runs: a bare status, never the owner's problem shape. The
        // binder's own 415 problem branch is covered by EndpointRequestBinderTests.
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.BodyShape>());

        using var content = new StringContent("id=x", Encoding.UTF8, "text/plain");
        var response = await host.Client.PostAsync("/items/x", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Content_type_gated_modes_reject_with_a_bare_415_and_no_body()
    {
        await using var host = await PipelineHost.StartAsync(api =>
            api.MapOperation<SampleBody>(
                "POST", "/gated", "Gated", EndpointBodyMode.RequiredWithContentType, ["application/json"],
                typeof(SampleResponse), 200, null,
                (context, _, _) => context.Response.WriteAsync("never")));

        using var content = new StringContent("""{"id":"x"}""", Encoding.UTF8, "text/plain");
        var response = await host.Client.PostAsync("/gated", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_payload_gated_mode_rejects_a_literal_null_body_with_a_bare_415_and_no_body()
    {
        await using var host = await PipelineHost.StartAsync(api =>
            api.MapOperation<SampleBody>(
                "POST", "/payload-gated", "PayloadGated", EndpointBodyMode.RequiredWithContentTypeAndPayload,
                ["application/json"], typeof(SampleResponse), 200, null,
                (context, _, _) => context.Response.WriteAsync("never")));

        var response = await host.Client.PostAsync("/payload-gated", Json("null"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_strict_typed_query_failure_names_the_wire_parameter()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.QueryShape>());

        var response = await host.Client.GetAsync("/queries?id=q&limit=invalid");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"limit\"", body);
        Assert.Contains("Value [invalid] is not valid for a [Int32] property!", body);
    }

    [Fact]
    public async Task A_failure_after_the_response_started_is_rethrown_not_rewritten()
    {
        // Once headers are on the wire (a streaming producer failing mid-response), no problem
        // document can be written; the original failure must surface, never a secondary
        // headers-already-sent mutation error.
        await using var host = await PipelineHost.StartAsync(api =>
            api.MapUnboundOperation("GET", "/started", "Started", null, StatusCodes.Status200OK, null,
                async context =>
                {
                    await context.Response.WriteAsync("partial");
                    await context.Response.Body.FlushAsync();
                    throw new InvalidOperationException("mid-stream failure");
                }));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.Client.GetAsync("/started"));

        Assert.Contains("mid-stream failure", exception.ToString());
        Assert.DoesNotContain("response has already started", exception.ToString());
    }

    // ---------- The failure ladder ----------

    [Fact]
    public async Task A_translated_domain_exception_writes_the_owners_problem()
    {
        await using var host = await PipelineHost.StartAsync(
            api => api.MapEndpoint<ShapeEndpoints.FaultingShape>(),
            services => services.AddSingleton<IEndpointExceptionTranslator, TestExceptionTranslator>());

        var response = await host.Client.GetAsync("/faulting/domain");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("unkeyed:promotion-conflict", body);
        Assert.Contains("domainErrors", body);
    }

    [Fact]
    public async Task A_fault_renderer_owns_its_exception_before_translation_runs()
    {
        await using var host = await PipelineHost.StartAsync(
            api => api.MapEndpoint<ShapeEndpoints.FaultingShape>(),
            services =>
            {
                services.AddSingleton<IEndpointFaultRenderer, TestFaultRenderer>();
                services.AddSingleton<IEndpointExceptionTranslator, TestExceptionTranslator>();
            });

        var response = await host.Client.GetAsync("/faulting/rendered");

        Assert.Equal(418, (int)response.StatusCode);
        Assert.Equal("rendered:unkeyed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_untranslated_exception_is_a_sanitized_500()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.FaultingShape>());

        var response = await host.Client.GetAsync("/faulting/unexpected");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Unexpected error occurred", body);
        Assert.DoesNotContain("secret-internal-details", body);
    }

    [Fact]
    public async Task Cancellation_is_rethrown_rather_than_contained()
    {
        await using var host = await PipelineHost.StartAsync(api => api.MapEndpoint<ShapeEndpoints.FaultingShape>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.GetAsync("/faulting/canceled"));
    }

    [Fact]
    public async Task An_owner_without_a_problem_writer_gets_the_sanitized_fallback_shape()
    {
        // No IEndpointProblemWriter is registered at all: the failure path must still answer with
        // the last-resort problem document rather than failing the failure path itself.
        await using var host = await PipelineHost.StartAsync(
            api => api.MapEndpoint<ShapeEndpoints.FaultingShape>(),
            registerProblemWriter: false);

        var response = await host.Client.GetAsync("/faulting/unexpected");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("\"status\":500", body);
        Assert.Contains("Unexpected error occurred", body);
    }

    // ---------- Owner-keyed resolution ----------

    [Fact]
    public async Task Owner_keyed_failure_services_win_over_unkeyed_fallbacks()
    {
        await using var host = await PipelineHost.StartAsync(
            api => api.MapEndpoint<ShapeEndpoints.FaultingShape>(),
            services =>
            {
                services.AddKeyedSingleton<IEndpointExceptionTranslator>("Test.Owner", (_, _) => new TestExceptionTranslator("keyed"));
                services.AddSingleton<IEndpointExceptionTranslator>(new TestExceptionTranslator("unkeyed"));
                services.AddKeyedSingleton<IEndpointProblemWriter>("Test.Owner", (_, _) => new TestProblemWriter("keyed"));
            });

        var response = await host.Client.GetAsync("/faulting/domain");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("keyed:promotion-conflict", body);
        Assert.Contains("\"writer\":\"keyed\"", body);
    }

    [Fact]
    public async Task Keyed_fault_renderers_are_consulted_for_their_own_group()
    {
        await using var host = await PipelineHost.StartAsync(
            api => api.MapEndpoint<ShapeEndpoints.FaultingShape>(),
            services => services.AddKeyedSingleton<IEndpointFaultRenderer>("Test.Owner", (_, _) => new TestFaultRenderer("keyed")));

        var response = await host.Client.GetAsync("/faulting/rendered");

        Assert.Equal(418, (int)response.StatusCode);
        Assert.Equal("rendered:keyed", await response.Content.ReadAsStringAsync());
    }
}
