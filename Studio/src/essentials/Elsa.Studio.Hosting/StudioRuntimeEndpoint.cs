using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using CShells.Lifecycle;
using Elsa.Studio.Core.Contracts;
using Elsa.Studio.Core.Events;

namespace Elsa.Studio.Hosting;

internal static partial class StudioRuntimeEndpoint
{
    internal static async Task<IResult> ReadAsync(HttpContext context, IConfiguration configuration, StudioHostingSettings settings)
    {
        try
        {
            var services = context.RequestServices;
            var authority = services.GetRequiredService<IStudioBackendAuthorityAccessor>().Value;
            await using var scope = await StudioShellActivation.AcquireScopeAsync(services.GetRequiredService<IShellRegistry>(), settings.ShellName, context.RequestAborted);
            var handlers = scope.ServiceProvider.GetServices<IStudioEventHandler<OnStudioRuntimeConfigurationCollecting>>().ToArray();
            if (handlers.Length != 1)
                throw new StudioHostingException("Studio runtime configuration requires exactly one collecting handler.");
            var collection = new OnStudioRuntimeConfigurationCollecting(context.Request.PathBase.Value ?? "");
            await handlers[0].Handle(collection, context.RequestAborted);
            var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in collection.Configuration)
                if (!NamespacePattern().IsMatch(entry.Namespace) || !extensions.TryAdd(entry.Namespace, entry.Value))
                    throw new StudioHostingException("Studio runtime configuration contains an invalid or duplicate namespace.");
            var value = StudioRuntimeScript.BuildRuntimeConfig(configuration, settings.AuthEnabled, authority);
            if (extensions.Count != 0) value.Add("extensions", extensions);
            // Serialize while the owning shell scope is alive. No scoped module object escapes the read.
            return Results.Content($"{StudioRuntimeScript.GlobalName} = {Serialize(value)};", "application/javascript");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StudioRuntimeEndpoint))
                .LogWarning(error, "The selected Studio shell could not project public runtime configuration.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static string Serialize(Dictionary<string, object?> value)
    {
        // Module-owned DTO metadata must die with this read, rather than enter the shared serializer cache.
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        return JsonSerializer.Serialize(value, options);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespacePattern();
}
