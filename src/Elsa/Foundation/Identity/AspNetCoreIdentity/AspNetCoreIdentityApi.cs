using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text.Json;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

/// <summary>Maps the ASP.NET Core Identity login protocol using ordinary ASP.NET Core endpoints.</summary>
public static class AspNetCoreIdentityApi
{
    private const string PublicCategory = "identity-protocol";
    private const string PublicReason = "First-party password sign-in protocol entry point.";

    public static void MapAspNetCoreIdentityApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var owner = typeof(AspNetCoreIdentityFeature).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The ASP.NET Core Identity assembly has no name.");
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet("/" + AspNetCoreIdentityDefaults.LoginRoute, HandleLoginPageAsync)
            .WithLoginMetadata(owner, descriptionMethod, "AspNetCoreIdentityLoginPage")
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(string), ["text/html"]))
            .AllowPublic(PublicCategory, PublicReason);

        endpoints.MapPost("/" + AspNetCoreIdentityDefaults.LoginRoute, HandleLoginAsync)
            .WithLoginMetadata(owner, descriptionMethod, "AspNetCoreIdentityLogin", typeof(AuthSession), includeRequest: true)
            .WithMetadata(new AcceptsMetadata(["application/json", "application/x-www-form-urlencoded"], typeof(LoginRequest), false))
            .AllowPublic(PublicCategory, PublicReason);
    }

    private static async Task HandleLoginPageAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value;
        var returnUrl = LocalUrl.Sanitize(context.Request.Query["returnUrl"].FirstOrDefault(), options.AllowedReturnUrlOrigins);
        var error = context.Request.Query["error"].FirstOrDefault();
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(LoginPage.Render(returnUrl, error is not null, tokens.RequestToken), context.RequestAborted);
    }

    private static async Task HandleLoginAsync(HttpContext context)
    {
        var request = await ReadLoginRequestAsync(context);
        if (request is null)
            return;

        var isFormFlow = !string.IsNullOrEmpty(request.ReturnUrl);
        if (RequiresAntiforgery(context) && !await IsAntiforgeryValidAsync(context))
        {
            if (isFormFlow)
            {
                await RedirectToLoginWithErrorAsync(context, request.ReturnUrl);
                return;
            }

            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value;
        var outcome = await context.RequestServices.GetRequiredService<IIdentitySignInService>()
            .PasswordSignInAsync(request.Username, request.Password, request.TenantId, context.RequestAborted);
        if (!outcome.Succeeded)
        {
            if (isFormFlow)
            {
                await RedirectToLoginWithErrorAsync(context, request.ReturnUrl);
                return;
            }

            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        if (isFormFlow)
        {
            await Results.Redirect(LocalUrl.Sanitize(request.ReturnUrl, options.AllowedReturnUrlOrigins), permanent: false)
                .ExecuteAsync(context);
            return;
        }

        await Results.Json(outcome.Session!, AspNetCoreIdentityJsonContext.Default.AuthSession).ExecuteAsync(context);
    }

    private static async Task<LoginRequest?> ReadLoginRequestAsync(HttpContext context)
    {
        try
        {
            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                return new LoginRequest
                {
                    Username = form["username"].FirstOrDefault() ?? string.Empty,
                    Password = form["password"].FirstOrDefault() ?? string.Empty,
                    TenantId = form["tenantId"].FirstOrDefault(),
                    ReturnUrl = form["returnUrl"].FirstOrDefault()
                };
            }

            return await context.Request.ReadFromJsonAsync<LoginRequest>(context.RequestAborted);
        }
        catch (JsonException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return null;
        }
        catch (InvalidDataException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return null;
        }
        catch (NotSupportedException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return null;
        }
        catch (InvalidOperationException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return null;
        }
    }

    private static bool RequiresAntiforgery(HttpContext context)
    {
        var contentType = context.Request.ContentType;
        if (string.IsNullOrEmpty(contentType))
            return true;

        return !MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            || !parsed.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsAntiforgeryValidAsync(HttpContext context)
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static async Task RedirectToLoginWithErrorAsync(HttpContext context, string? returnUrl)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value;
        var back = $"/{AspNetCoreIdentityDefaults.LoginRoute}?error=1&returnUrl={Uri.EscapeDataString(LocalUrl.Sanitize(returnUrl, options.AllowedReturnUrlOrigins))}";
        await Results.Redirect(back, permanent: false).ExecuteAsync(context);
    }

    private static IEndpointConventionBuilder WithLoginMetadata(
        this IEndpointConventionBuilder builder,
        string owner,
        System.Reflection.MethodInfo descriptionMethod,
        string operationId,
        Type? responseType = null,
        bool includeRequest = false)
    {
        builder.WithOwner(owner).WithAuthoringModel(EndpointAuthoringModels.MinimalApi);
        var metadata = new List<object>
        {
            descriptionMethod,
            new EndpointNameMetadata(operationId),
            new TagsAttribute("Identity")
        };
        if (responseType is not null)
            metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, responseType, ["application/json"]));
        if (includeRequest)
            metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []));
        builder.WithMetadata(metadata.ToArray());
        return builder;
    }
}
