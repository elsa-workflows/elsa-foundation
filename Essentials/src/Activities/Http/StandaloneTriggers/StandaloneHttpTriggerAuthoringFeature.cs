using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.StandaloneTriggers;

/// <summary>HTTP trigger authoring metadata only; this feature installs no HTTP listener or runtime source.</summary>
[ShellFeature(name: "StandaloneHttpTriggerAuthoring", DisplayName = "HTTP trigger authoring", Description = "Describe and validate independent HTTP entry points.")]
public class StandaloneHttpTriggerAuthoringFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStandaloneTriggerTypeSource, HttpTriggerTypeSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITriggerConfigurationValidator, HttpTriggerConfigurationValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITriggerReservationPlanner, HttpTriggerReservationPlanner>());
    }
}

internal sealed class HttpTriggerTypeSource : IStandaloneTriggerTypeSource
{
    public IEnumerable<TriggerTypeDescriptor> GetTypes()
    {
        yield return new("http", 1, "HTTP", "Http",
            [new("method", "Method", Required: true, DefaultValue: JsonSerializer.SerializeToElement("POST"),
                Choices: [new("GET", "GET"), new("POST", "POST"), new("PUT", "PUT"), new("PATCH", "PATCH"), new("DELETE", "DELETE")]),
             new("path", "Route", Required: true, Description: "A route relative to the configured HTTP endpoint mount."),
             new("sourceAccess", "Source access", Required: true, DefaultValue: JsonSerializer.SerializeToElement("Authenticated"),
                Choices: [new("Authenticated", "Authenticated caller"), new("Anonymous", "Public endpoint")])],
            [new(["payload"], "Request payload", "object"), new(["metadata", "method"], "HTTP method", "string"),
             new(["metadata", "path"], "Request path", "string")], "One exclusive endpoint with an explicit response mode.");
    }
}

internal sealed class HttpTriggerConfigurationValidator : ITriggerConfigurationValidator
{
    public string TypeKey => "http";
    public ValueTask<IReadOnlyList<TriggerFinding>> ValidateAsync(int schemaVersion, JsonElement configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var findings = new List<TriggerFinding>();
        if (schemaVersion != 1) findings.Add(new("UnsupportedSchema", "The HTTP configuration version is unsupported."));
        if (!configuration.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String ||
            method.GetString() is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS"))
            findings.Add(new("InvalidMethod", "Choose a supported HTTP method.", "method"));
        var path = configuration.TryGetProperty("path", out var pathValue) && pathValue.ValueKind == JsonValueKind.String ? pathValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Contains('?') || path.Contains('#') || path.Any(char.IsControl))
            findings.Add(new("InvalidRoute", "Enter a route without a query, fragment or control characters.", "path"));
        else
        {
            try { _ = RoutePatternFactory.Parse(path); }
            catch (RoutePatternException) { findings.Add(new("InvalidRoute", "The route template is invalid.", "path")); }
        }
        if (!configuration.TryGetProperty("sourceAccess", out var sourceAccess) || sourceAccess.ValueKind != JsonValueKind.String ||
            sourceAccess.GetString() is not ("Authenticated" or "Anonymous"))
            findings.Add(new("InvalidSourceAccess", "Choose whether this endpoint requires an authenticated caller.", "sourceAccess"));
        if (!configuration.TryGetProperty("http", out var http) || http.ValueKind != JsonValueKind.Object ||
            !http.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String || mode.GetString() is not ("WorkflowResponse" or "AsyncFanOut"))
            findings.Add(new("ResponseModeRequired", "Choose the HTTP response mode.", "http.mode"));
        else if (mode.GetString() == "AsyncFanOut")
        {
            if (http.TryGetProperty("admissionPolicy", out var policy) && (policy.ValueKind != JsonValueKind.String || policy.GetString() is not ("RequireAll" or "AcceptAvailable")))
                findings.Add(new("InvalidAdmissionPolicy", "Choose the asynchronous admission policy.", "http.admissionPolicy"));
        }
        else if (http.TryGetProperty("admissionPolicy", out _))
            findings.Add(new("UnexpectedAdmissionPolicy", "Admission policy applies to asynchronous HTTP only.", "http.admissionPolicy"));
        return ValueTask.FromResult<IReadOnlyList<TriggerFinding>>(findings);
    }
}
