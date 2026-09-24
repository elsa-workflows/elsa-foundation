using System.Reflection;
using System.Text.Json;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Version-2 operations over one previously created host-owned configuration snapshot.</summary>
internal static class EfToolingContextOperation
{
    private const string InspectContext = "inspect-context";
    private static readonly byte[] Newline = "\n"u8.ToArray();

    internal static async Task<int> RunAsync(
        Stream request,
        Stream response,
        EfToolingConfigurationContext context,
        IEnumerable<Assembly> assemblies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assemblies);

        string? command = null;
        EfToolingContextResponse result;
        try
        {
            var parsed = await ReadAsync(request, cancellationToken);
            command = parsed.Command;
            if (parsed.Version != EfToolingContextContract.Version)
                throw EfToolingRefusal.Usage("unsupported-request-version", "This host does not support the supplied context operation version.");
            if (command != InspectContext && !EfToolingCommands.All.Contains(command, StringComparer.Ordinal))
                throw EfToolingRefusal.Usage("unknown-command", "The context operation command is not supported.");
            if (command != InspectContext)
                throw EfToolingRefusal.Resolution("context-operation-unavailable", "This context operation is not yet available in this host build.");

            ValidateInspection(parsed, context);
            var closure = assemblies.Where(assembly => !assembly.IsDynamic).Distinct().ToArray();
            var hostAssembly = closure.FirstOrDefault(assembly =>
                StringComparer.Ordinal.Equals(assembly.GetName().Name, context.HostName) &&
                SamePath(assembly.Location, Path.Join(context.HostDirectory, $"{context.HostName}.dll")));
            if (hostAssembly is null)
                throw EfToolingRefusal.Resolution("host-composition-unavailable", "The selected host assembly is not loaded in this tooling context.");

            var defaults = context.CreateHostDefaults(hostAssembly);
            var inspection = context.InspectUnselectedHost(defaults, closure, cancellationToken);
            result = new EfToolingContextResponse
            {
                Command = command,
                InspectContext = new EfToolingInspectContextPayload { Outcome = inspection.Outcome },
                ConfigurationContext = new EfToolingConfigurationContextFacts
                {
                    Source = context.Source,
                    Environment = context.Environment,
                    Shell = context.Shell,
                    Resource = null,
                    Resolution = inspection.Outcome,
                    Unresolved = inspection.UnresolvedCodes
                }
            };
        }
        catch (EfToolingRefusal refusal)
        {
            result = Failed(command, refusal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            // Reflection, discovery, and source failures can contain paths or configuration values.
            result = Failed(command, EfToolingRefusal.Resolution(
                "configuration-context-invalid", "The selected host context could not be inspected."));
        }

        await JsonSerializer.SerializeAsync(response, result, EfToolingContextContract.Json, cancellationToken);
        await response.WriteAsync(Newline, cancellationToken);
        await response.FlushAsync(cancellationToken);
        return result.ExitCode;
    }

    private static async Task<EfToolingContextRequest> ReadAsync(Stream request, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw EfToolingRefusal.Usage("invalid-request", "A context operation request must be an object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in document.RootElement.EnumerateObject())
                if (!fields.Add(field.Name))
                    throw EfToolingRefusal.Usage("invalid-request", "The context operation request repeats a field.");
            return document.RootElement.Deserialize<EfToolingContextRequest>(EfToolingContextContract.Json)
                   ?? throw EfToolingRefusal.Usage("invalid-request", "The context operation request is empty.");
        }
        catch (JsonException)
        {
            throw EfToolingRefusal.Usage("invalid-request", "The context operation request is not valid JSON.");
        }
    }

    private static void ValidateInspection(EfToolingContextRequest request, EfToolingConfigurationContext context)
    {
        if (context.ExplicitSelection || context.Shell is not null ||
            request.Resource is not null || request.Provider is not null || request.Schema is not null ||
            request.Output is not null || request.Engine is not null || request.Packages is not null ||
            request.Connection is not null)
            throw EfToolingRefusal.Usage("invalid-request", "An unselected-host inspection accepts no resource, provider, script, or database fields.");

        var selection = request.Selection;
        if (selection is null)
            return;
        if (selection.Kind is EfToolingSelection.AllKind or EfToolingSelection.FromHostKind)
        {
            if (selection.Modules is null)
                return;
        }
        else if (selection.Kind == EfToolingSelection.ModulesKind &&
                 selection.Modules is { Count: > 0 } modules &&
                 modules.All(module => !string.IsNullOrWhiteSpace(module)) &&
                 modules.Distinct(StringComparer.OrdinalIgnoreCase).Count() == modules.Count)
            return;
        throw EfToolingRefusal.Usage("invalid-request", "The inspection module selection is not valid.");
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static EfToolingContextResponse Failed(string? command, EfToolingRefusal refusal) => new()
    {
        Status = "error",
        ExitCode = refusal.ExitCode,
        Command = command,
        Error = new EfToolingErrorPayload
        {
            Code = refusal.Code,
            Message = refusal.Message,
            Details = refusal.Details
        }
    };
}
