using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;

namespace Elsa.Cli;

/// <summary>Produces one safe preview or a fixed refusal from a candidate-bound host response.</summary>
public sealed class CandidateInspectionOutput
{
    private static readonly string[] ResolutionFields =
    [
        "source", "shell", "environment", "resolution", "selection", "participants", "configuredValueAffinity",
        "targetVerification", "runtimeParity", "packageReachability", "connectivity", "schemaReadiness",
        "migrationReadiness", "activation", "externalInputs", "unresolved"
    ];
    private static readonly string[] SelectionFields =
        ["acceptedFeatureIds", "requestedFeatureIds", "effectiveFeatureIds", "disabledFeatureIds", "implicitFeatureIds"];
    private static readonly string[] ParticipantFields =
        ["feature", "module", "selection", "resource", "provider", "connectionReference", "selectorScope", "resourceScope", "exactFileProvenance"];

    /// <summary>Validates correlation and renders only safe plan/configuration projections in a fixed order.</summary>
    /// <exception cref="CliRefusal">The response is invalid, the host refused, or the output format is unsupported.</exception>
    public string Render(CompositionInspectionCapture capture, JsonElement hostResponse,
        int processExitCode, string format = "text")
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (format is not ("text" or "json"))
            throw CliRefusal.Usage("composition-format-invalid", "The output format must be text or json.");
        capture.VerifyUnchanged();
        var environmentLane = capture.HasEnvironmentInput;
        try
        {
            if (environmentLane)
                WorkerContract.ValidateCandidateEnvironmentHostResponse(hostResponse, capture.Payload, processExitCode);
            else
                WorkerContract.ValidateCandidateHostResponse(hostResponse, capture.Payload, processExitCode);
        }
        catch (WorkerRefusal refusal) { throw new CliRefusal(refusal.ExitCode, refusal.Code, refusal.Message); }
        if (hostResponse.GetProperty("status").GetString() == "refused")
        {
            var error = hostResponse.GetProperty("error");
            var code = error.GetProperty("code").GetString()!;
            var details = new List<string>();
            if (error.TryGetProperty("feature", out var feature)) details.Add($"Feature: {feature.GetString()}");
            if (error.TryGetProperty("resource", out var resource)) details.Add($"Resource: {resource.GetString()}");
            throw new CliRefusal(processExitCode, code, HostRefusalMessage(code, error), details);
        }
        var projection = CompositionPlanCommand.Project(capture.Plan, null);
        var source = hostResponse.GetProperty("configurationResolution");
        var body = CopyFields(source, ResolutionFields);
        body["selection"] = CopyFields(source.GetProperty("selection"), SelectionFields);
        body["participants"] = new JsonArray(source.GetProperty("participants").EnumerateArray()
            .Select(row => (JsonNode?)CopyFields(row, ParticipantFields)).ToArray());
        var rendered = format == "json"
            ? JsonSerializer.Serialize(new { plan = projection, configurationResolution = body }, CompositionPlanCommand.JsonOptions)
            : CompositionPlanCommand.RenderText(projection) + Environment.NewLine +
              RenderConfiguration(JsonSerializer.SerializeToElement(body));
        capture.VerifyUnchanged();
        return rendered;
    }

    private static JsonObject CopyFields(JsonElement source, IReadOnlyList<string> fields)
    {
        var result = new JsonObject();
        foreach (var name in fields) result[name] = JsonSerializer.SerializeToNode(source.GetProperty(name));
        return result;
    }

    private static string HostRefusalMessage(string code, JsonElement error) => code switch
    {
        "candidate-request-invalid" => "The candidate inspection request is invalid.",
        "candidate-request-too-large" => "The candidate inspection request exceeds the supported size limit.",
        "candidate-capture-invalid" => "The captured configuration files are invalid.",
        "candidate-environment-input-invalid" => "The explicit environment input is invalid.",
        "candidate-environment-input-too-large" => "The explicit environment input exceeds the supported size limit.",
        "candidate-environment-key-collision" => "The explicit environment input contains colliding keys.",
        "candidate-environment-prefix-unsupported" => "The explicit environment input contains an unsupported service prefix.",
        "candidate-environment-host-unenrolled" => "The selected host is not enrolled for explicit environment inspection.",
        "candidate-selection-conflict" when error.TryGetProperty("reason", out var reason) => reason.GetString() switch
        {
            "unknown" => "The accepted selection contains a feature unknown to the selected host.",
            "unavailable" => "An accepted feature is unavailable in the selected host.",
            "requested-extra" => "The captured configuration requests a feature outside the accepted selection.",
            "requested-missing" => "The captured configuration omits an accepted feature.",
            "expanded-extra" => "Host dependency expansion adds a feature outside the accepted selection.",
            "required-disabled" => "A required feature is explicitly disabled.",
            "case-collision" => "The selected feature identities have a case collision.",
            _ => "The accepted selection conflicts with the selected host configuration."
        },
        "candidate-selection-conflict" => "The accepted selection conflicts with the selected host configuration.",
        "resource-selection-invalid" => "A persistence resource selection is invalid.",
        "resource-not-found" => "A selected persistence resource is not defined.",
        "resource-definition-invalid" => "A selected persistence resource has an invalid definition.",
        "resource-configurator-unsupported" => "A selected resource cannot be combined with a custom persistence configurator.",
        "resource-required-feature-disabled" => "A feature required by the persistence layout is disabled.",
        "resource-legacy-conflict" => "A resource selection conflicts with explicit legacy persistence settings.",
        "resource-ownership-unresolved" => "Ownership of the selected persistence layout could not be established.",
        "resource-context-conflict" => "The selected persistence targets conflict with a shared context or transaction requirement.",
        _ => "The captured candidate configuration was refused."
    };

    private static string RenderConfiguration(JsonElement resolution)
    {
        var lines = new List<string> { "Configuration resolution:" };
        foreach (var property in resolution.EnumerateObject())
        {
            if (property.Name == "selection")
            {
                lines.Add("  Selection:");
                foreach (var set in property.Value.EnumerateObject())
                    lines.Add($"    {set.Name}: {string.Join(", ", set.Value.EnumerateArray().Select(value => value.GetString()))}");
            }
            else if (property.Name == "participants")
            {
                lines.Add("  Persistence participants:");
                foreach (var row in property.Value.EnumerateArray())
                {
                    lines.Add($"    {row.GetProperty("feature").GetString()} -> {row.GetProperty("module").GetString()}");
                    foreach (var field in row.EnumerateObject().Where(field => field.Name is not ("feature" or "module")))
                        lines.Add($"      {field.Name}: {field.Value.GetString() ?? "not projected"}");
                }
                if (property.Value.GetArrayLength() == 0) lines.Add("    none");
            }
            else if (property.Name == "unresolved")
                lines.Add($"  Unresolved: {string.Join(", ", property.Value.EnumerateArray().Select(value => value.GetString()))}");
            else
                lines.Add($"  {property.Name}: {property.Value.GetString()}");
        }
        return string.Join(Environment.NewLine, lines);
    }

}
