using System.Reflection;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.ResourceResolution;

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
            if (command == InspectContext)
                ValidateInspection(parsed, context);
            else if (!context.ExplicitSelection || context.Shell is null)
                throw EfToolingRefusal.Usage("invalid-request", "A context operation requires an explicitly selected shell.");
            var closure = assemblies.Where(assembly => !assembly.IsDynamic).Distinct().ToArray();
            var hostAssembly = closure.FirstOrDefault(assembly =>
                StringComparer.Ordinal.Equals(assembly.GetName().Name, context.HostName) &&
                EfToolingConfigurationContext.SameHostAssemblyPath(assembly.Location, context.HostDirectory, context.HostName));
            if (hostAssembly is null)
                throw EfToolingRefusal.Resolution("host-composition-unavailable", "The selected host assembly is not loaded in this tooling context.");

            var defaults = context.CreateHostDefaults(hostAssembly);
            if (command is EfToolingCommands.List or EfToolingCommands.Plan)
                result = RunOffline(parsed, context, defaults!, closure, cancellationToken);
            else if (command == EfToolingCommands.Script)
                result = RunScript(parsed, context, defaults!, closure, cancellationToken);
            else if (command is EfToolingCommands.Apply or EfToolingCommands.Validate or EfToolingCommands.PostMigrate)
                result = await RunLiveAsync(parsed, context, defaults!, closure, cancellationToken);
            else if (command == InspectContext)
            {
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
            else
                throw EfToolingRefusal.Resolution("context-operation-unavailable", "This context operation is not yet available in this host build.");
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
                "configuration-context-invalid", "The selected host context operation could not be completed."));
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

    private static void ValidateList(EfToolingContextRequest request)
    {
        if (request.Provider is not null || request.Schema is not null || request.Output is not null ||
            request.Engine is not null || request.Packages is not null || request.Connection is not null)
            throw EfToolingRefusal.Usage("invalid-request", "A context list accepts no provider, script, or database fields.");
    }

    private static EfToolingContextResponse RunOffline(
        EfToolingContextRequest request,
        EfToolingConfigurationContext context,
        IEfToolingShellDefaults defaults,
        IReadOnlyList<Assembly> closure,
        CancellationToken cancellationToken)
    {
        var isPlan = request.Command == EfToolingCommands.Plan;
        if (isPlan)
            ValidatePlan(request);
        else
            ValidateList(request);

        var (prepared, selected, selectedSet) = SelectModules(request, context, defaults, closure, cancellationToken);
        var provider = isPlan ? CheckProviderAgreement(request.Provider!, prepared, selectedSet, context) : null;
        EfToolingResponse? planned = null;
        if (provider is not null)
        {
            try
            {
                planned = EfToolingHost.PlanModules(selected, provider, request.Schema, cancellationToken);
            }
            catch (EfToolingRefusal refusal) when (refusal.Code == "provider-engine-unavailable")
            {
                throw EfToolingRefusal.Resolution(refusal.Code,
                    "The selected provider engine is unavailable in this host closure.");
            }
        }

        return new EfToolingContextResponse
        {
            Command = request.Command,
            List = isPlan ? null : EfToolingHost.ListModules(selected).List,
            Plan = planned?.Plan,
            ConfigurationContext = BuildFacts(context, request.Resource, prepared, selectedSet, false)
        };
    }

    private static async Task<EfToolingContextResponse> RunLiveAsync(
        EfToolingContextRequest request,
        EfToolingConfigurationContext context,
        IEfToolingShellDefaults defaults,
        IReadOnlyList<Assembly> closure,
        CancellationToken cancellationToken)
    {
        if (request.Selection is null || string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.Connection) || request.Output is not null ||
            request.Engine is not null || request.Packages is not null)
            throw EfToolingRefusal.Usage("invalid-request", "A live context operation requires selection, provider, and connection, without script or package fields.");

        var (prepared, selected, selectedSet) = SelectModules(request, context, defaults, closure, cancellationToken);
        var provider = CheckProviderAgreement(request.Provider, prepared, selectedSet, context);
        var selectedParticipants = prepared.ResolvedParticipants
            .Where(participant => participant.Selection != PersistenceSelectionKind.Legacy &&
                participant.Participant.ModuleNames.Any(selectedSet.Contains))
            .ToArray();
        if (selectedParticipants.Length > 0 && request.Resource is null)
            throw EfToolingRefusal.Resolution("resource-required", "A live operation over a persistence resource requires an explicit resource selection.");

        EfToolingResponse result;
        try
        {
            result = await EfToolingHost.RunLiveModulesAsync(selected, request.Command!, provider,
                request.Schema, request.Connection, () =>
                {
                    // The host has checked all options. Verify every selected owner before it can
                    // construct a DbContext or open a connection for any selected module.
                    foreach (var name in selectedParticipants.Select(participant => participant.ConnectionName)
                                 .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
                        context.VerifyExpectedConnection(name!, request.Connection);
                }, cancellationToken);
        }
        catch (EfToolingRefusal refusal) when (refusal.Code == "provider-engine-unavailable")
        {
            throw EfToolingRefusal.Resolution(refusal.Code,
                "The selected provider engine is unavailable in this host closure.");
        }
        return new EfToolingContextResponse
        {
            Command = request.Command,
            Apply = result.Apply,
            Validate = result.Validate,
            PostMigrate = result.PostMigrate,
            ConfigurationContext = BuildFacts(context, request.Resource, prepared, selectedSet,
                selectedParticipants.Length > 0)
        };
    }

    private static EfToolingContextResponse RunScript(
        EfToolingContextRequest request,
        EfToolingConfigurationContext context,
        IEfToolingShellDefaults defaults,
        IReadOnlyList<Assembly> closure,
        CancellationToken cancellationToken)
    {
        if (request.Selection is null || string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.Output) || request.Engine is null || request.Packages is null ||
            request.Connection is not null)
            throw EfToolingRefusal.Usage("invalid-request", "A context script requires selection, provider, output, engine and package facts, without a database connection.");

        var (prepared, selected, selectedSet) = SelectModules(request, context, defaults, closure, cancellationToken);
        var provider = CheckProviderAgreement(request.Provider, prepared, selectedSet, context);
        if (request.Resource is null && prepared.ResolvedParticipants.Any(participant =>
                participant.Selection != PersistenceSelectionKind.Legacy &&
                participant.Participant.ModuleNames.Any(selectedSet.Contains)))
            throw EfToolingRefusal.Resolution("resource-required", "A script over a persistence resource requires an explicit resource selection.");

        var facts = BuildFacts(context, request.Resource, prepared, selectedSet, false);
        EfToolingResponse scripted;
        try
        {
            scripted = EfToolingHost.ScriptModules(selected, provider, request.Schema, request.Output,
                request.Engine, request.Packages,
                new EfToolingHostFacts
                {
                    Name = context.HostName,
                    ProviderAgreement = EfToolingProviderAgreement.Checked,
                    Shell = context.Shell,
                    Environment = context.Environment
                }, facts, cancellationToken);
        }
        catch (EfToolingRefusal refusal) when (refusal.Code == "provider-engine-unavailable")
        {
            throw EfToolingRefusal.Resolution(refusal.Code,
                "The selected provider engine is unavailable in this host closure.");
        }
        catch (EfToolingRefusal refusal) when (refusal.Code is "module-generation-failed" or "output-write-failed")
        {
            throw EfToolingRefusal.Resolution(refusal.Code,
                "The context script could not be generated or written for the selected host.");
        }

        return new EfToolingContextResponse
        {
            Command = request.Command,
            Script = scripted.Script,
            ConfigurationContext = facts
        };
    }

    private static (EfPersistencePreparationResult Prepared, IReadOnlyList<EfModuleDescriptor> Selected,
        HashSet<string> SelectedSet) SelectModules(
        EfToolingContextRequest request,
        EfToolingConfigurationContext context,
        IEfToolingShellDefaults defaults,
        IReadOnlyList<Assembly> closure,
        CancellationToken cancellationToken)
    {
        var prepared = context.PrepareShell(defaults, closure, cancellationToken);
        var discovered = EfToolingHost.Discover(closure);
        var names = EfToolingTargetSelection.Select(prepared,
            discovered.Select(module => module.Name).ToArray(), request.Selection, request.Resource);
        var selected = EfModuleOrder.Sort(names.Select(name => EfModuleCatalog.Find(discovered, name)!).ToArray());
        return (prepared, selected, selected.Select(module => module.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static EfToolingConfigurationContextFacts BuildFacts(
        EfToolingConfigurationContext context,
        string? resource,
        EfPersistencePreparationResult prepared,
        IReadOnlySet<string> selectedSet,
        bool targetMatched) => new()
    {
        Source = context.Source,
        Environment = context.Environment,
        Shell = context.Shell,
        Resource = resource,
        Resolution = prepared.ResolvedParticipants.Any(participant =>
            participant.Selection != PersistenceSelectionKind.Legacy &&
            participant.Participant.ModuleNames.Any(selectedSet.Contains)) ? "resource" : "legacy",
        TargetVerification = targetMatched ? "matched" : "not-performed",
        Participants = prepared.ResolvedParticipants
            .Where(participant => participant.Participant.ModuleNames.Any(selectedSet.Contains))
            .SelectMany(participant => participant.Participant.ModuleNames.Where(selectedSet.Contains),
                (participant, module) => new EfToolingContextParticipant
                {
                    Feature = participant.Participant.FeatureId,
                    Module = module,
                    Resource = participant.ResourceName,
                    Provider = participant.Provider,
                    ConnectionReference = participant.ConnectionName,
                    Selection = participant.Selection.ToString()
                })
            .OrderBy(participant => participant.Module, StringComparer.Ordinal)
            .ThenBy(participant => participant.Feature, StringComparer.Ordinal)
            .ToArray(),
        Unresolved = prepared.UnresolvedCodes
            .Where(code => !targetMatched || code != "expected-connection-unchecked")
            .Order(StringComparer.Ordinal).ToArray()
    };

    private static void ValidatePlan(EfToolingContextRequest request)
    {
        if (request.Selection is null || string.IsNullOrWhiteSpace(request.Provider) ||
            request.Output is not null || request.Engine is not null || request.Packages is not null ||
            request.Connection is not null)
            throw EfToolingRefusal.Usage("invalid-request", "A context plan requires selection and provider, without script or database fields.");
    }

    private static string CheckProviderAgreement(
        string requested,
        EfPersistencePreparationResult prepared,
        IReadOnlySet<string> selectedModules,
        EfToolingConfigurationContext context)
    {
        string provider;
        try
        {
            provider = EfRelationalProviderBinding.ExpectedProviderName(requested);
        }
        catch (ArgumentException)
        {
            throw EfToolingRefusal.Usage("unknown-provider", "The requested provider is not supported.");
        }

        var targets = prepared.ResolvedParticipants.ToDictionary(
            participant => participant.Participant.FeatureId, StringComparer.OrdinalIgnoreCase);
        var disagreement = prepared.HostFeatureUsages
            .Where(usage => usage.DeclaresProvider && usage.Modules.Any(selectedModules.Contains))
            .Any(usage =>
            {
                var configured = targets.TryGetValue(usage.Feature, out var participant) &&
                                 participant.Selection != PersistenceSelectionKind.Legacy
                    ? participant.Provider
                    : prepared.ConfiguredProviders.GetValueOrDefault(usage.Feature);
                var effective = string.IsNullOrWhiteSpace(configured) ? EfProviderAgreement.UnsetProvider : configured;
                return !StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(effective),
                    EfRelationalProviderBinding.Normalize(provider));
            });
        if (disagreement || CapabilityDisagrees(context, provider))
            throw EfToolingRefusal.Resolution("provider-disagreement",
                "The requested provider disagrees with an enabled owner or this host's provider selection.");
        return provider;
    }

    private static bool CapabilityDisagrees(EfToolingConfigurationContext context, string provider)
    {
        var section = context.Configuration.GetSection(EfProviderAgreement.CapabilityKey);
        var value = section.Value;
        var option = section["Option"];
        if (value is null && option is null)
        {
            if (section.GetChildren().Any())
                throw EfToolingRefusal.Resolution("capability-selection-invalid", "The host provider selection has an unsupported shape.");
            return false;
        }
        if (value is not null && option is not null && !StringComparer.Ordinal.Equals(value, option))
            throw EfToolingRefusal.Resolution("capability-selection-invalid", "The host provider selection is ambiguous.");
        var options = (option ?? value)!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (options.Length == 0)
            throw EfToolingRefusal.Resolution("capability-selection-invalid", "The host provider selection is empty.");
        return EfProviderAgreement.CheckCapabilitySelection(options, provider) is not null;
    }

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
