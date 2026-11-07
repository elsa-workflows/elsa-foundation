using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Processing.Core;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Dashboard.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Acme.Processing;

public static class ProcessingPermissions
{
    public const string Read = "acme/processing:read";
    public const string Create = "acme/processing:create";
    public const string CategoryRead = "acme/processing/category:read";
    public const string Query = "acme/processing-questions:read";
}

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Examples")]
[ShellFeature(name: "AcmeProcessing", DisplayName = "Acme Processing Reference Module",
    Description = "Owns reference processing records and explicitly contributes authorized business dimensions to questions.", DependsOn = ["StudioDashboardApi"])]
public class ProcessingFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        if (services.Count(item => item.ServiceType == typeof(IProcessingActorContext)) > 1)
            throw new InvalidOperationException("Exactly one processing actor context must be registered.");
        services.TryAddScoped<IProcessingActorContext, StudioProcessingActorContext>();
        services.TryAddSingleton<ProcessingMemoryState>();
        services.TryAddScoped<IProcessingRecordReader, InMemoryProcessingStore>();
        services.TryAddScoped<IProcessingRecordWriter, InMemoryProcessingStore>();
        services.TryAddScoped<ProcessingAccess>();
        services.TryAddScoped<IProcessingRecords, ProcessingRecords>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionContributor, ProcessingPermissionExtension>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardDatasetExtension, ProcessingDashboardSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardQueryValidationExtension, ProcessingDashboardSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardQueryExecutionExtension, ProcessingDashboardSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDashboardQueryDrillExtension, ProcessingDashboardSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ProcessingCompositionGuard>());
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
        endpoints.MapPost("/_acme/processing/records", async (HttpContext http, IProcessingRecords records) =>
            await Boundary(async () =>
            {
                if (http.Request.ContentLength > 16384) throw new ProcessingException("validation", "The processing request exceeds its bound.");
                using var bounded = new MemoryStream();
                var buffer = new byte[1024];
                int read;
                while ((read = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) != 0)
                {
                    if (bounded.Length + read > 16384) throw new ProcessingException("validation", "The processing request exceeds its bound.");
                    bounded.Write(buffer, 0, read);
                }
                var input = JsonSerializer.Deserialize<ProcessingCreate>(bounded.ToArray(), options)
                    ?? throw new ProcessingException("validation", "A processing record is required.");
                return Results.Json(new { id = await records.CreateAsync(input, http.RequestAborted) });
            }));
        endpoints.MapGet("/_acme/processing/records/{id}", async (HttpContext http, IProcessingRecords records, string id) =>
            await Boundary(async () => await records.FindAsync(id, http.RequestAborted) is { } record ? Results.Json(record) : Results.NotFound()));
        endpoints.MapGet("/_acme/processing/records", async (HttpContext http, IProcessingRecords records) =>
            await Boundary(async () =>
            {
                var take = int.TryParse(http.Request.Query["take"], out var requested) ? requested : 25;
                return Results.Json(await records.ReadAsync(take, http.Request.Query["cursor"].FirstOrDefault(), http.RequestAborted));
            }));
    }

    private static async Task<IResult> Boundary(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ProcessingException exception) { return Results.Json(new { code = exception.Code, message = exception.Message }, statusCode: exception.Code == "denied" ? 403 : 400); }
        catch (JsonException) { return Results.Json(new { code = "validation", message = "The processing input is invalid." }, statusCode: 400); }
    }
}

internal sealed class ProcessingCompositionGuard(IServiceProvider provider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = provider.CreateScope();
        RequireOne(scope.ServiceProvider.GetServices<IProcessingActorContext>());
        RequireOne(scope.ServiceProvider.GetServices<IProcessingRecordReader>());
        RequireOne(scope.ServiceProvider.GetServices<IProcessingRecordWriter>());
        RequireOne(scope.ServiceProvider.GetServices<IProcessingRecords>());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void RequireOne<T>(IEnumerable<T> services)
    {
        if (services.Take(2).Count() != 1)
            throw new InvalidOperationException($"Exactly one {typeof(T).Name} must be configured for Acme Processing.");
    }
}

internal sealed class ProcessingPermissionExtension : IPermissionContributor
{
    public string OwnerId => "Acme.Processing";
    public IEnumerable<Permission> Contribute() =>
    [new(ProcessingPermissions.Read, "Read processing records", "Acme Processing", "Read individually authorized processing records."),
     new(ProcessingPermissions.Create, "Create processing records", "Acme Processing", "Create owned reference processing records in the current tenant."),
     new(ProcessingPermissions.CategoryRead, "Read customer categories", "Acme Processing", "Read deliberately exposed customer-category values."),
     new(ProcessingPermissions.Query, "Query processing records", "Acme Processing", "Ask aggregate questions independently of record and category grants.")];
}
