using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Studio.Api.Authorization;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Core.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Backends;

/// <summary>Host-owned connection metadata; never a proxy to arbitrary backend addresses.</summary>
internal static partial class StudioBackendProfiles
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Elsa.Studio.Core.Models.StudioBackendAuthority, InMemoryStudioPreferenceStore> Stores = new();
    internal const string Namespace = "backend-profiles";
    internal const string TenantClaim = StudioBridgeAuthorization.TenantClaimType;
    private const string Path = "/_elsa/studio/backends/profiles";

    internal static void Add(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStudioPreferenceNamespace, BackendProfilesPreferenceNamespace>());
        services.TryAddSingleton<IStudioPreferenceNamespaceRegistry, StudioPreferenceNamespaceRegistry>();
        services.TryAddSingleton<IStudioPreferenceStore>(provider => provider.GetService<Elsa.Studio.Core.Contracts.IStudioBackendAuthorityAccessor>() is { } authority
            ? Stores.GetValue(authority.Value, _ => new InMemoryStudioPreferenceStore())
            : new InMemoryStudioPreferenceStore());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IStudioPreferenceService, StudioPreferenceService>();
    }

    internal static string BrowserAuthority(IConfiguration configuration) =>
        configuration["Studio:BackendBaseUrl"] is {Length: > 0} legacy ? legacy :
        (configuration.GetSection("Studio:Backends:Profiles").GetChildren().FirstOrDefault(profile =>
            configuration["Studio:Backends:DefaultId"] is null || profile["Id"] == configuration["Studio:Backends:DefaultId"])?["BaseUrl"] ?? "");

    internal static object ReadConfiguration(IConfiguration configuration)
    {
        var profiles = new List<object>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in configuration.GetSection("Studio:Backends:Profiles").GetChildren())
        {
            var id = section["Id"] ?? "";
            var name = section["Name"] ?? "";
            var baseUrl = section["BaseUrl"] ?? "";
            var environment = section["Environment"] ?? "Development";
            if (!ValidId(id) || !ids.Add(id) || string.IsNullOrWhiteSpace(name) || name.Length > 80 || !ValidUrl(baseUrl)
                || environment is not ("Development" or "Test" or "Production"))
                throw new InvalidOperationException("Studio:Backends:Profiles contains an invalid or duplicate connection profile.");
            profiles.Add(new { id, name, baseUrl, environment, authenticationProfile = section["AuthenticationProfile"], source = "configuration" });
        }
        if (profiles.Count > 32) throw new InvalidOperationException("Studio accepts at most 32 configured backend profiles.");
        var defaultId = configuration["Studio:Backends:DefaultId"];
        if (defaultId is not null && !ids.Contains(defaultId)) throw new InvalidOperationException("Studio:Backends:DefaultId must reference a configured profile.");
        var authenticationProfiles = configuration.GetSection("Studio:Backends:AuthenticationProfiles").GetChildren()
            .ToDictionary(section => section.Key, section => (object)new
            {
                enabled = section.GetValue("Enabled", true),
                tokenEndpoint = section["TokenEndpoint"],
                refreshEndpoint = section["RefreshEndpoint"]
            }, StringComparer.Ordinal);
        return new
        {
            defaultId,
            allowPersonalProfiles = configuration.GetValue("Studio:Backends:AllowPersonalProfiles", true),
            profiles,
            authenticationProfiles
        };
    }

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, ReadAsync)
            .RequireAuthorization(StudioBridgeAuthorization.BuildPermissionPolicy("studio/preferences:read"));
        endpoints.MapPut(Path, WriteAsync)
            .RequireAuthorization(StudioBridgeAuthorization.BuildPermissionPolicy("studio/preferences:write"));
    }

    private static StudioPreferenceKey? Scope(HttpContext context)
    {
        // The demo bridge ticket is deliberately not an identity for shared server persistence.
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenant = context.User.FindFirstValue(TenantClaim);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenant)) return null;
        var host = context.RequestServices.GetRequiredService<IConfiguration>()["Studio:HostId"] ?? "default";
        return new(subject, tenant, host, Namespace);
    }

    private static async Task<IResult> ReadAsync(HttpContext context, IStudioPreferenceService preferences)
    {
        var scope = Scope(context);
        if (scope is null) return Results.Unauthorized();
        var document = await preferences.FindAsync(scope, context.RequestAborted);
        if (document is null) return Results.NotFound();
        context.Response.Headers.ETag = $"\"{document.Revision}\"";
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(document);
    }

    private static async Task<IResult> WriteAsync(HttpContext context, IStudioPreferenceService preferences, IConfiguration configuration)
    {
        if (!configuration.GetValue("Studio:Backends:AllowPersonalProfiles", true)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var scope = Scope(context);
        if (scope is null) return Results.Unauthorized();
        if (context.Request.ContentLength > 34 * 1024) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + count > 34 * 1024) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                body.Write(buffer, 0, count);
            }
            var request = JsonSerializer.Deserialize<ProfileWrite>(body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 16, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            if (request is null || !new BackendProfilesPreferenceNamespace().Validate(request.Value)) return Results.BadRequest();
            var configured = configuration.GetSection("Studio:Backends:Profiles").GetChildren().Select(section => section["Id"]).ToHashSet(StringComparer.Ordinal);
            if (request.Value.GetProperty("profiles").EnumerateArray().Any(profile => configured.Contains(profile.GetProperty("id").GetString())))
                return Results.BadRequest(new { error = "A personal profile cannot replace a configuration-managed profile." });
            var match = context.Request.Headers.IfMatch.ToString();
            var create = context.Request.Headers.IfNoneMatch.ToString();
            if ((string.IsNullOrWhiteSpace(match) && create != "*") || (!string.IsNullOrEmpty(match) && !string.IsNullOrEmpty(create)))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
            if (!string.IsNullOrEmpty(match) && (match.Length < 3 || !match.StartsWith('"') || !match.EndsWith('"') || match.Contains(','))) return Results.BadRequest();
            var condition = string.IsNullOrEmpty(match) ? StudioPreferenceWriteCondition.MustNotExist : StudioPreferenceWriteCondition.Matches(match[1..^1]);
            var document = await preferences.WriteAsync(scope, new(request.SchemaVersion, request.Value), condition, context.RequestAborted);
            context.Response.Headers.ETag = $"\"{document.Revision}\"";
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(document);
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (StudioPreferenceConflictException error) { return Results.Conflict(new { error = error.Message }); }
        catch (StudioPreferenceException error) { return Results.BadRequest(new { error = error.Message }); }
    }

    private sealed record ProfileWrite(int SchemaVersion, JsonElement Value);

    internal static bool ValidId(string value) => IdPattern().IsMatch(value) && value != "legacy";
    internal static bool ValidUrl(string value) => value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && !value.Contains('\\');
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}

internal sealed class BackendProfilesPreferenceNamespace : IStudioPreferenceNamespace
{
    public string Name => StudioBackendProfiles.Namespace;
    public int CurrentSchemaVersion => 1;
    public int MaxBytes => 32 * 1024;
    public JsonElement? Migrate(JsonElement value, int fromSchemaVersion) => fromSchemaVersion == 1 && Validate(value) ? value.Clone() : null;

    public bool Validate(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(property => property.Name is not ("profiles" or "lastSelectedId"))) return false;
        if (!value.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() > 32) return false;
        if (value.TryGetProperty("lastSelectedId", out var last) && (last.ValueKind != JsonValueKind.String || last.GetString()!.Length > 64)) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles.EnumerateArray())
        {
            if (profile.ValueKind != JsonValueKind.Object || profile.EnumerateObject().Any(property => property.Name is not ("id" or "name" or "baseUrl" or "environment" or "authenticationProfile"))) return false;
            if (!Text(profile, "id", out var id) || !StudioBackendProfiles.ValidId(id) || !ids.Add(id)
                || !Text(profile, "name", out var name) || string.IsNullOrWhiteSpace(name) || name.Length > 80
                || !Text(profile, "baseUrl", out var url) || !StudioBackendProfiles.ValidUrl(url)
                || !Text(profile, "environment", out var environment) || environment is not ("Development" or "Test" or "Production")) return false;
            if (profile.TryGetProperty("authenticationProfile", out var auth) && (auth.ValueKind != JsonValueKind.String || auth.GetString()!.Length > 64)) return false;
        }
        return true;
    }

    private static bool Text(JsonElement value, string name, out string result)
    {
        result = "";
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        result = property.GetString()!;
        return true;
    }
}
