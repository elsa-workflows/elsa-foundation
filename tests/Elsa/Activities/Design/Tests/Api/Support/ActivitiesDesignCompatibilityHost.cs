using System.Collections;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CShells.FastEndpoints.Contracts;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Tests.Api.Support;

/// <summary>
/// A real FastEndpoints host with deterministic transport seams.  The production feature, endpoint
/// discovery, policy metadata, API Explorer and JSON/OpenAPI descriptions are all used unchanged; only
/// domain senders are replaced so the historical capture is independent of a persistence provider.
/// </summary>
public sealed class ActivitiesDesignCompatibilityHost(IHost host) : IAsyncDisposable
{
    public HttpClient Client { get; } = host.GetTestClient();
    public IHost Host { get; } = host;

    public static async Task<ActivitiesDesignCompatibilityHost> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "elsa-activities-design-before-capture");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddAuthentication(CaptureAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CaptureAuthenticationHandler>(CaptureAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>([CaptureAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                    services.AddOpenApi();
                    new ActivitiesDesignApiFeature().ConfigureServices(services);
                    services.AddSingleton<CaptureRequestSender>();
                    services.AddSingleton<IRequestSender>(provider => provider.GetRequiredService<CaptureRequestSender>());
                    services.AddSingleton<CaptureCommandSender>();
                    services.AddSingleton<ICommandSender>(provider => provider.GetRequiredService<CaptureCommandSender>());
                    services.AddFastEndpoints(options => options.Assemblies = [typeof(ActivitiesDesignApiFeature).Assembly]);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints(config =>
                        {
                            using var scope = endpoints.ServiceProvider.CreateScope();
                            foreach (var configurator in scope.ServiceProvider.GetServices<IFastEndpointsConfigurator>())
                                configurator.Configure(config);
                            config.Security.PermissionsClaimType = IdentityClaimTypes.Permission;
                        });
                        endpoints.MapOpenApi();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return new ActivitiesDesignCompatibilityHost(host);
    }

    public async Task<string> GetOpenApiAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        Host.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class CaptureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ActivitiesDesignBeforeCapture";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = Request.Headers[ActivitiesDesignCompatibilityCases.IdentityHeader].ToString();
        if (string.IsNullOrWhiteSpace(identity))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new ClaimsIdentity(Scheme.Name);
        claims.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
        claims.AddClaim(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
        claims.AddClaim(new Claim(IdentityClaimTypes.TenantId, "capture-tenant"));
        claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, "capture-actor"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name)));
    }
}

internal sealed class CaptureRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
{
    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
    {
        var scenario = contextAccessor.HttpContext?.Request.Headers[ActivitiesDesignCompatibilityCases.IdentityHeader].ToString();
        if (scenario == "trusted-domain-not-found")
            throw new EntityNotFoundException("The deterministic capture entity was not found.");
        if (scenario == "trusted-domain-conflict")
            throw new ActivityAuthoringException(409, "capture.domain-conflict", "Capture domain conflict", "The deterministic capture domain conflict was requested.");
        if (scenario == "trusted-domain-failure")
            throw new ActivityAuthoringException(422, "capture.domain-failure", "Capture domain failure", "The deterministic capture domain failure was requested.");
        if (scenario == "trusted-cancellation")
            throw new OperationCanceledException("The deterministic capture request was canceled.", cancellationToken);

        return Task.FromResult((T)CaptureResponseFactory.Create(typeof(T)));
    }
}

internal sealed class CaptureCommandSender(IHttpContextAccessor contextAccessor) : ICommandSender
{
    public Task<T> Send<T>(Elsa.Mediator.Core.Contracts.ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
    {
        var scenario = contextAccessor.HttpContext?.Request.Headers[ActivitiesDesignCompatibilityCases.IdentityHeader].ToString();
        if (scenario == "trusted-domain-not-found")
            throw new EntityNotFoundException("The deterministic capture entity was not found.");
        if (scenario == "trusted-domain-conflict")
            throw new ActivityAuthoringException(409, "capture.domain-conflict", "Capture domain conflict", "The deterministic capture domain conflict was requested.");
        if (scenario == "trusted-domain-failure")
            throw new ActivityAuthoringException(422, "capture.domain-failure", "Capture domain failure", "The deterministic capture domain failure was requested.");
        if (scenario == "trusted-cancellation")
            throw new OperationCanceledException("The deterministic capture command was canceled.", cancellationToken);

        return Task.FromResult((T)CaptureResponseFactory.Create(typeof(T)));
    }

    public Task Send(Elsa.Mediator.Core.Contracts.ICommand command, CancellationToken cancellationToken = default)
    {
        var scenario = contextAccessor.HttpContext?.Request.Headers[ActivitiesDesignCompatibilityCases.IdentityHeader].ToString();
        return scenario switch
        {
            "trusted-domain-not-found" => Task.FromException(new EntityNotFoundException("The deterministic capture entity was not found.")),
            "trusted-domain-conflict" => Task.FromException(new ActivityAuthoringException(409, "capture.domain-conflict", "Capture domain conflict", "The deterministic capture domain conflict was requested.")),
            "trusted-domain-failure" => Task.FromException(new ActivityAuthoringException(422, "capture.domain-failure", "Capture domain failure", "The deterministic capture domain failure was requested.")),
            "trusted-cancellation" => Task.FromException(new OperationCanceledException("The deterministic capture command was canceled.", cancellationToken)),
            _ => Task.CompletedTask
        };
    }
}

internal static class CaptureResponseFactory
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    public static object Create(Type type) => Create(type, 0) ?? throw new InvalidOperationException($"Cannot create capture response '{type.FullName}'.");

    private static object? Create(Type type, int depth)
    {
        if (depth > 8)
            return null;
        if (type == typeof(string))
            return "capture";
        if (type == typeof(DateTimeOffset))
            return FixedTime;
        if (type == typeof(DateTime))
            return FixedTime.UtcDateTime;
        if (type == typeof(JsonElement))
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
        if (type == typeof(CancellationToken))
            return CancellationToken.None;
        if (type == typeof(Guid))
            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0);
        if (Nullable.GetUnderlyingType(type) is not null)
            return null;
        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return Activator.CreateInstance(type);
        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var elementType = type.GetGenericArguments().SingleOrDefault();
            if (elementType is not null)
                return Array.CreateInstance(elementType, 0);
        }
        if (type == typeof(object) || type.IsInterface || type.IsAbstract)
            return null;
        if (type.IsValueType)
            return Activator.CreateInstance(type);

        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();
        if (constructor is null)
            return Activator.CreateInstance(type);

        var arguments = constructor.GetParameters()
            .Select(parameter => CreateParameter(parameter.ParameterType, depth + 1))
            .ToArray();
        return constructor.Invoke(arguments);
    }

    private static object? CreateParameter(Type type, int depth)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            var arguments = type.GetGenericArguments();
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
        }

        return Create(type, depth);
    }
}
