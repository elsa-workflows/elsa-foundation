using Elsa.Activities.Runtime.Core.Models;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

internal sealed class CaptureTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => FixedTime;
}

internal sealed class CaptureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PublishingBeforeCapture";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString();
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
        var scenario = contextAccessor.HttpContext?.Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString();
        cancellationToken.ThrowIfCancellationRequested();
        if (scenario == "trusted-cancellation")
            throw new OperationCanceledException("The deterministic capture request was canceled.", cancellationToken);
        if (scenario == "trusted-domain-not-found")
            throw new EntityNotFoundException("The deterministic capture entity was not found.");
        if (scenario == "trusted-domain-conflict")
        {
            throw new PublicationPolicyResolutionException(
                "expected_publication_mismatch",
                "The deterministic publication conflict was requested.");
        }
        if (scenario == "trusted-domain-validation")
            throw new PublicationPolicyResolutionException("invalid_host_policy", "The deterministic publication validation failure was requested.");
        if (scenario == "trusted-expression-errors")
            throw new ExpressionPublicationValidationException(new(
                ExpressionDraftValidationState.Errors,
                [],
                "expression-validation-errors"));
        if (scenario == "trusted-expression-unavailable")
            throw new ExpressionPublicationValidationException(new(
                ExpressionDraftValidationState.Unavailable,
                [],
                "expression-validation-unavailable"));
        if (scenario == "trusted-generic-500")
            throw new InvalidOperationException("The deterministic unexpected failure was requested.");

        if (typeof(T) == typeof(PublishedWorkflowView))
        {
            var response = new PublishedWorkflowView(
                "publication-capture",
                "definition-capture",
                "version-route",
                "definition-version-capture",
                "artifact-capture",
                "default",
                PublicationStatusView.Active,
                "source-reference-capture",
                new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
                null,
                "1.0",
                "artifact-hash-capture",
                "root-capture",
                1,
                WasCreated: true);
            return Task.FromResult((T)(object)response);
        }

        return Task.FromResult((T)CaptureResponseFactory.Create(typeof(T)));
    }
}

/// <summary>Deterministic compiler seam used only by the historical host; it still returns a real executable.</summary>
internal sealed class CaptureWorkflowExecutableCompiler : IWorkflowExecutableCompiler
{
    public ValueTask<WorkflowExecutable> CompileAsync(
        WorkflowExecutableCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse("{}");
        var node = new ExecutableNode(
            "capture-node",
            "capture-activity",
            "Capture.Activity",
            "1.0",
            new RuntimeActivityDescriptor("capture.consumer", "1", document.RootElement),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        var executable = new WorkflowExecutable(
            new WorkflowExecutableIdentity(
                "capture-artifact",
                "capture-definition",
                request.VersionId,
                "1.0",
                "capture-hash"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            new IncidentStrategyReference("default", "1"));
        return ValueTask.FromResult(executable);
    }
}

internal static class CaptureResponseFactory
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    public static object Create(Type type) => Create(type, 0) ?? throw new InvalidOperationException($"Cannot create capture response '{type.FullName}'.");

    private static object? Create(Type type, int depth)
    {
        if (depth > 10)
            return null;
        if (type == typeof(string))
            return "capture";
        if (type == typeof(DateTimeOffset))
            return FixedTime;
        if (type == typeof(DateTime))
            return FixedTime.UtcDateTime;
        if (type == typeof(Guid))
            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (type == typeof(JsonElement))
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0);
        if (Nullable.GetUnderlyingType(type) is not null)
            return null;
        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var elementType = type.GetGenericArguments().SingleOrDefault();
            if (elementType is not null)
                return Array.CreateInstance(elementType, 0);
        }
        if (type == typeof(object))
            return new { value = "capture" };
        if (type.IsInterface || type.IsAbstract)
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
