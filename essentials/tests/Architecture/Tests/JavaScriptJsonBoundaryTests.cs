using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.JavaScript;
using Elsa.Workflows.Runtime.JavaScript.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JavaScriptJsonBoundaryTests
{
    [Theory]
    [InlineData("{ \"OrderTotal\":1.00, \"text\":\"\\u003C雪\", \"explicit\":null, \"large\":9007199254740993 }")]
    [InlineData("\"René <>&\"")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task Production_script_endpoint_preserves_raw_results_and_null_independently_of_host_options(string? raw)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddElsaEndpoints();
        builder.Services.AddAuthentication(BoundaryAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, BoundaryAuthentication>(BoundaryAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { BoundaryAuthentication.SchemeName });
        builder.Services.AddSingleton<IPermissionContributor, JavaScriptExecutionPermissionContributor>();
        builder.Services.AddSingleton<IJavaScriptScriptEvaluator>(new Evaluator(raw));
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.WriteIndented = true;
            options.SerializerOptions.Encoder = JavaScriptEncoder.Default;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });
        await using var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        JavaScriptExecutionApi.MapJavaScriptExecutionApi(app);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-S9-Permission", JavaScriptExecutionPermissions.Execute);
        using var response = await client.PostAsync("/javascript/execute", new StringContent("{\"script\":\"return 1;\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(raw ?? "null", document.RootElement.GetProperty("value").GetRawText());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var wrongCase = await client.PostAsync("/javascript/execute", new StringContent("{\"Script\":\"return 1;\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCase.StatusCode);
    }

    private sealed class Evaluator(string? raw) : IJavaScriptScriptEvaluator
    {
        public ValueTask<JavaScriptScriptEvaluationResult> EvaluateAsync(JavaScriptScriptEvaluationRequest request)
        {
            Assert.Equal("return 1;", request.Source);
            if (raw is null) return ValueTask.FromResult(new JavaScriptScriptEvaluationResult(null));
            using var document = JsonDocument.Parse(raw);
            return ValueTask.FromResult(new JavaScriptScriptEvaluationResult(document.RootElement));
        }
    }

    private sealed class BoundaryAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "S9JavaScriptBoundary";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers["X-S9-Permission"] != JavaScriptExecutionPermissions.Execute)
                return Task.FromResult(AuthenticateResult.NoResult());
            Claim[] claims = [new(ClaimTypes.NameIdentifier, "operator"), new(IdentityClaimTypes.Permission, JavaScriptExecutionPermissions.Execute),
                new(IdentityClaimTypes.Normalized, "v1"), new(IdentityClaimTypes.TenantId, "tenant-a")];
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
