using System.CommandLine;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Cli;

/// <summary>Shared safe input and diagnostic boundary for the file-only composition commands.</summary>
internal static class CompositionFileBridgeOutput
{
    public static Option<string> Required(string name, string description) =>
        new(name) { Description = description, Required = true };

    public static string ReadInput(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw CliRefusal.Resolution("bridge-source-unreadable", "A required local input file could not be read.");
        }
    }

    public static object[] SafeFindings(SelectionPlan plan) => plan.Findings.Select(finding =>
    {
        RequireSafe(finding.Code);
        RequireSafe(finding.Severity);
        if (finding.FeatureId is not null)
            RequireSafe(finding.FeatureId);
        if (finding.DependencyId is not null)
            RequireSafe(finding.DependencyId);
        return (object)new { finding.Code, finding.Severity, finding.FeatureId, finding.DependencyId };
    }).ToArray();

    public static IEnumerable<CompositionCandidateChange> SafeChanges(IEnumerable<CompositionCandidateChange> changes)
    {
        foreach (var change in changes)
        {
            RequireSafe(change.FeatureId);
            RequireSafe(change.Pointer);
            RequireSafe(change.ValueType);
            RequireSafe(change.SourceLayer);
            yield return change;
        }
    }

    public static int Guarded(Func<int> action, string operation)
    {
        try
        {
            return action();
        }
        catch (CliRefusal refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, refusal.Message, refusal.Details);
            return refusal.ExitCode;
        }
        catch (CshellsSourceException refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, $"The selected CShells source cannot be {operation}.", []);
            return ToolExitCode.Refusal;
        }
        catch (CompositionImportException refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, $"The selected local composition cannot be {operation} safely.", []);
            return ToolExitCode.Refusal;
        }
        catch (SettingReviewException refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, "The local setting review cannot be used.", []);
            return ToolExitCode.Refusal;
        }
        catch (SelectionDocumentException)
        {
            Report.WriteRefusal(Console.Error, "bridge-source-invalid", "The supplied catalog or authored composition is invalid.", []);
            return ToolExitCode.Refusal;
        }
        catch (OperationCanceledException)
        {
            Report.WriteRefusal(Console.Error, "bridge-review-required", "The local composition review was cancelled.", []);
            return ToolExitCode.Refusal;
        }
        catch (Exception)
        {
            Report.WriteRefusal(Console.Error, "bridge-output-failed", $"The composition could not be {operation}.", []);
            return ToolExitCode.ResolutionFailure;
        }
    }

    public static void RequireSafe(string value)
    {
        if (!SelectionValueRules.IsSafeReference(value))
            throw CliRefusal.Usage("bridge-source-invalid", "The source produced an unsafe review identity.");
    }
}
