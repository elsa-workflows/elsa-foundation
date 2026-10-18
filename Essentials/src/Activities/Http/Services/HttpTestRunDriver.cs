using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Activities.Http.Activities;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Services;

/// <summary>Prepares a real HTTP request against the isolated ingress for one exact HttpEndpoint start binding.</summary>
internal sealed partial class HttpTestRunDriver(IHttpContextAccessor? httpContextAccessor = null) : ITestRunTriggerDriver
{
    public const string StableDriverId = "Elsa.HttpEndpoint";
    public const string RequestModeId = "http-request";

    private static readonly JsonElement RequestSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "routeValues": { "type": "object", "additionalProperties": { "type": "string" } },
            "query": { "type": "object", "additionalProperties": { "type": "array", "items": { "type": "string" } } },
            "headers": { "type": "object", "additionalProperties": { "type": "array", "items": { "type": "string" } } },
            "body": { "type": ["string", "null"] }
          }
        }
        """).RootElement.Clone();

    public string DriverId => StableDriverId;

    public bool Supports(TestRunTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return StringComparer.Ordinal.Equals(context.ProviderId, StableDriverId)
               && StringComparer.Ordinal.Equals(context.Node.ActivityType, HttpEndpoint.ActivityType)
               && StringComparer.Ordinal.Equals(context.Binding.StimulusType, HttpEndpointStimulus.StimulusType)
               && TryGetRoute(context.Binding, out _, out _);
    }

    public IReadOnlyCollection<TestRunTriggerMode> Describe(TestRunTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Supports(context) || !TryGetRoute(context.Binding, out var template, out var method))
            return Array.Empty<TestRunTriggerMode>();

        var configuration = JsonSerializer.SerializeToElement(new { template, method });
        return
        [
            new TestRunTriggerMode(
                RequestModeId,
                "Send an HTTP request",
                "Send a real request to this draft's isolated test endpoint.",
                isDefault: true,
                configurationVersion: 1,
                configurationSchema: RequestSchema,
                configuration: configuration)
        ];
    }

    public TestRunTriggerValidation Validate(
        TestRunTriggerContext context,
        string modeId,
        JsonElement? data)
    {
        ArgumentNullException.ThrowIfNull(context);
        var findings = new List<TestRunTriggerFinding>();
        if (!Supports(context) || !TryGetRoute(context.Binding, out var template, out _))
            return Failure("unsupported-trigger", "This HTTP trigger cannot be driven by a test request.");
        if (!StringComparer.Ordinal.Equals(modeId, RequestModeId))
            return Failure("unsupported-mode", "The selected HTTP request mode is not supported.");

        if (data is { ValueKind: JsonValueKind.Undefined })
            return Failure("invalid-request", "The HTTP request configuration is invalid.");

        var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        string? body = null;
        if (data is { ValueKind: not JsonValueKind.Null })
        {
            var element = data.Value;
            if (element.ValueKind != JsonValueKind.Object)
                return Failure("invalid-request", "The HTTP request configuration must be an object.");

            foreach (var property in element.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "routeValues":
                        if (!ReadStringObject(property.Value, routeValues))
                            findings.Add(Finding("invalid-route-values", "Route values must map names to strings.", "routeValues"));
                        break;
                    case "query":
                        if (!ReadRepeatedValues(property.Value, query))
                            findings.Add(Finding("invalid-query", "Query values must map names to arrays of strings.", "query"));
                        break;
                    case "headers":
                        if (!ReadRepeatedValues(property.Value, headers))
                            findings.Add(Finding("invalid-headers", "Header values must map names to arrays of strings.", "headers"));
                        break;
                    case "body":
                        if (property.Value.ValueKind == JsonValueKind.String)
                            body = property.Value.GetString();
                        else if (property.Value.ValueKind != JsonValueKind.Null)
                            findings.Add(Finding("invalid-body", "The request body must be text or null.", "body"));
                        break;
                    default:
                        findings.Add(Finding("unknown-field", "The HTTP request configuration contains an unsupported field.", property.Name));
                        break;
                }
            }
        }

        if (headers.Keys.Any(IsTransportCredentialHeader))
            findings.Add(Finding("protected-header", "Authentication headers are supplied by the authenticated API transport.", "headers"));
        foreach (var (name, values) in headers)
        {
            if (!IsValidHeaderName(name) || values.Any(value => value.Any(character => character is '\r' or '\n' || char.IsControl(character) && character != '\t')))
                findings.Add(Finding("invalid-header", "Header names and values must be valid HTTP header text.", "headers"));
        }

        if (!TryResolveRoutePath(template, routeValues, out _, out var routeError))
            findings.Add(Finding(routeError, "Route values must satisfy the authored route parameters.", "routeValues"));

        return findings.Count == 0
            ? TestRunTriggerValidation.Success()
            : TestRunTriggerValidation.Failure(findings.DistinctBy(finding => (finding.Code, finding.Field)));
    }

    public ValueTask<TestRunTriggerPreparation> PrepareAsync(
        TestRunTriggerPrepareContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var validation = Validate(context.Trigger, context.ModeId, context.Data);
        if (!validation.IsValid)
            throw new ArgumentException("The HTTP test request configuration is invalid.", nameof(context));
        if (!TryGetRoute(context.Trigger.Binding, out var template, out var method)
            || !TryReadRouteValues(context.Data, out var routeValues))
            throw new InvalidOperationException("The selected HTTP trigger binding is invalid.");
        if (!TryResolveRoutePath(template, routeValues, out var resolvedPath, out _))
            throw new ArgumentException("The HTTP test request route values are invalid.", nameof(context));

        var relativeUrl = $"/workflows/test-runs/{Uri.EscapeDataString(context.TestRunId)}/http";
        if (resolvedPath.Length > 0)
            relativeUrl += "/" + resolvedPath;

        // The action URL is server-root-relative. Preserve the host's deployment prefix so Studio can resolve it
        // against the API origin without guessing or appending an API route prefix.
        var pathBase = httpContextAccessor?.HttpContext?.Request.PathBase.Value?.TrimEnd('/');
        if (!string.IsNullOrEmpty(pathBase))
            relativeUrl = pathBase + relativeUrl;

        var safeDetails = JsonSerializer.SerializeToElement(new { template, method });
        var action = new TestRunDriverAction("http-request", relativeUrl, method, safeDetails);
        return ValueTask.FromResult(TestRunTriggerPreparation.External(action, details: safeDetails));
    }

    private static TestRunTriggerValidation Failure(string code, string message) =>
        TestRunTriggerValidation.Failure([Finding(code, message)]);

    private static TestRunTriggerFinding Finding(string code, string message, string? field = null) =>
        new(code, message, field);

    private static bool TryGetRoute(WorkflowTriggerBinding binding, out string template, out string method)
    {
        template = string.Empty;
        method = string.Empty;
        if (!binding.Metadata.TryGetValue(HttpEndpointRouting.TemplateMetadataKey, out var authoredTemplate)
            || !binding.Metadata.TryGetValue(HttpEndpointRouting.MethodMetadataKey, out var authoredMethod)
            || string.IsNullOrWhiteSpace(authoredTemplate)
            || string.IsNullOrWhiteSpace(authoredMethod)
            || authoredMethod.Any(character => !IsTokenCharacter(character)))
            return false;

        try
        {
            template = HttpEndpointStimulus.NormalizeTemplate(authoredTemplate);
        }
        catch (ArgumentException)
        {
            return false;
        }

        method = authoredMethod.ToUpperInvariant();
        return true;
    }

    private static bool ReadStringObject(JsonElement element, IDictionary<string, string> destination)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var valid = true;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { } value)
            {
                valid = false;
                continue;
            }
            destination[property.Name] = value;
        }
        return valid;
    }

    private static bool ReadRepeatedValues(JsonElement element, IDictionary<string, string[]> destination)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var valid = true;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array
                || property.Value.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
            {
                valid = false;
                continue;
            }
            destination[property.Name] = property.Value.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        }
        return valid;
    }

    private static bool TryReadRouteValues(JsonElement? data, out Dictionary<string, string> routeValues)
    {
        routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (data is null || data.Value.ValueKind == JsonValueKind.Null)
            return true;
        if (data.Value.ValueKind != JsonValueKind.Object)
            return false;
        return !data.Value.TryGetProperty("routeValues", out var values) || ReadStringObject(values, routeValues);
    }

    private static bool TryResolveRoutePath(
        string template,
        IReadOnlyDictionary<string, string> routeValues,
        out string resolvedPath,
        out string errorCode)
    {
        var tokens = RouteParameterPattern().Matches(template).Cast<Match>().Select(match => ParseRouteParameter(match.Groups["parameter"].Value)).ToArray();
        var knownNames = tokens.Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (routeValues.Keys.Any(name => !knownNames.Contains(name)))
        {
            resolvedPath = string.Empty;
            errorCode = "unknown-route-value";
            return false;
        }

        foreach (var parameter in tokens)
        {
            if (!routeValues.TryGetValue(parameter.Name, out var value))
            {
                if (!parameter.Optional && parameter.DefaultValue is null)
                {
                    resolvedPath = string.Empty;
                    errorCode = "missing-route-value";
                    return false;
                }
                continue;
            }

            if (value.Length == 0 || value.Any(character => char.IsControl(character) || character is '\\' or '?'))
            {
                resolvedPath = string.Empty;
                errorCode = "invalid-route-value";
                return false;
            }
            if (!parameter.CatchAll && value.Contains('/'))
            {
                resolvedPath = string.Empty;
                errorCode = "invalid-route-value";
                return false;
            }
            if (parameter.CatchAll && value.Split('/').Any(segment => segment is "." or ".." or ""))
            {
                resolvedPath = string.Empty;
                errorCode = "invalid-route-value";
                return false;
            }
        }

        var valuesWithDefaults = routeValues.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in tokens)
            if (!valuesWithDefaults.ContainsKey(parameter.Name) && parameter.DefaultValue is { } defaultValue)
                valuesWithDefaults[parameter.Name] = defaultValue;

        var segments = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resolvedSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            var resolvedSegment = RouteParameterPattern().Replace(segment, match =>
            {
                var parameter = ParseRouteParameter(match.Groups["parameter"].Value);
                if (!valuesWithDefaults.TryGetValue(parameter.Name, out var value))
                    return string.Empty;
                return parameter.CatchAll
                    ? string.Join('/', value.Split('/').Select(Uri.EscapeDataString))
                    : Uri.EscapeDataString(value);
            });
            if (resolvedSegment.Length > 0)
                resolvedSegments.Add(resolvedSegment);
        }

        resolvedPath = string.Join('/', resolvedSegments);
        errorCode = string.Empty;
        return true;
    }

    private static RouteParameterInfo ParseRouteParameter(string token)
    {
        var catchAll = token.StartsWith("**", StringComparison.Ordinal) || token.StartsWith('*');
        var body = token.TrimStart('*');
        var separator = body.IndexOfAny([':', '=', '?']);
        var name = separator < 0 ? body : body[..separator];
        var defaultStart = body.IndexOf('=');
        var defaultValue = defaultStart >= 0
            ? body[(defaultStart + 1)..].TrimEnd('?')
            : null;
        var optional = body.EndsWith('?') || defaultValue is not null;
        return new RouteParameterInfo(name, catchAll, optional, defaultValue);
    }

    private static bool IsTransportCredentialHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidHeaderName(string name) =>
        name.Length > 0 && name.All(IsTokenCharacter);

    private static bool IsTokenCharacter(char character) =>
        character is >= '!' and <= '~' && !"()<>@,;:\"/[]?={}".Contains(character);

    [GeneratedRegex(@"\{(?<parameter>[^{}]+)\}")]
    private static partial Regex RouteParameterPattern();

    private sealed record RouteParameterInfo(string Name, bool CatchAll, bool Optional, string? DefaultValue);
}
