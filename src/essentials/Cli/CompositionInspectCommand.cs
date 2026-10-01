using System.CommandLine;
using Elsa.Cli.Worker;

namespace Elsa.Cli;

/// <summary>Inspects one accepted captured candidate through the explicitly trusted installed host.</summary>
internal static class CompositionInspectCommand
{
    public static Command Build()
    {
        var host = new Option<string>("--host") { Description = "Installed framework-dependent host output.", Required = true };
        var source = new Option<string>("--host-dir") { Description = "Configuration source directory.", Required = true };
        var shell = new Option<string>("--shell") { Description = "Selected shell.", Required = true };
        var environment = new Option<string>("--environment") { Description = "Selected environment.", Required = true };
        var composition = new Option<string>("--composition") { Description = "Accepted authored composition JSON.", Required = true };
        var catalog = new Option<string>("--catalog") { Description = "Optional pinned selection catalog JSON." };
        var profiles = new Option<string[]>("--workspace-profile") { Description = "Optional workspace-profile JSON. Repeatable." };
        var review = new Option<string>("--setting-review") { Description = "Optional reviewed setting changes JSON." };
        var packages = new Option<string[]>("--packages") { Description = "Already installed package root. Repeatable." };
        var trust = new Option<bool>("--trust-host-code") { Description = "Permit the selected host's declared composer to execute in the inspection child." };
        var timeout = new Option<int?>("--timeout-seconds") { Description = "Inspection timeout, 1 to 300 seconds. Defaults to 60." };
        var format = new Option<string>("--format") { Description = "Output format: text or json. Defaults to text." };
        var command = new Command("inspect", "Preview effective persistence for an accepted candidate without publishing it.")
        { host, source, shell, environment, composition, catalog, profiles, review, packages, trust, timeout, format };

        command.SetAction((result, cancellationToken) => Guarded(async () =>
        {
            if (!result.GetValue(trust))
                throw CliRefusal.Usage("candidate-trust-required", "Candidate inspection requires explicit trust in the selected host code.");
            var outputFormat = result.GetValue(format) ?? "text";
            if (outputFormat is not ("text" or "json"))
                throw CliRefusal.Usage("composition-format-invalid", "The output format must be text or json.");
            var seconds = result.GetValue(timeout) ?? 60;
            if (seconds is < 1 or > 300)
                throw CliRefusal.Usage("candidate-request-invalid", "The inspection timeout must be between 1 and 300 seconds.");
            cancellationToken.ThrowIfCancellationRequested();

            HostLayout layout;
            string[] roots;
            try
            {
                layout = HostLayout.Resolve(result.GetRequiredValue(host));
                roots = (result.GetValue(packages) ?? []).Select(Path.GetFullPath).ToArray();
            }
            catch (Exception failure) when (failure is CliRefusal or IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
            {
                throw CliRefusal.Resolution("candidate-host-unavailable", "The selected installed host layout could not be inspected.");
            }

            var capture = CompositionInspectionCapture.Open(result.GetRequiredValue(source),
                result.GetRequiredValue(shell), result.GetRequiredValue(environment), result.GetRequiredValue(composition),
                result.GetValue(catalog), result.GetValue(review), result.GetValue(profiles));
            var request = new WorkerRequest
            {
                Command = WorkerCommands.InspectCandidate,
                HostDirectory = layout.Directory,
                HostName = layout.Name,
                DepsFile = layout.DepsFile,
                PackageRoots = roots,
                Candidate = capture.Payload
            };
            cancellationToken.ThrowIfCancellationRequested();
            capture.VerifyUnchanged();
            string rendered;
            try
            {
                var response = await new CandidateWorkerProcess().RunAsync(layout, request, seconds, cancellationToken);
                if (response.Error is { } error)
                    throw new CliRefusal(response.ExitCode, error.Code, error.Message);
                rendered = new CandidateInspectionOutput().Render(capture, response.Tooling!.Value, response.ExitCode, outputFormat);
            }
            catch (Exception failure) when (IsNonFatal(failure))
            {
                // Owned-process cleanup has already completed or refused. Recheck before reporting any
                // bounded outcome, so a refusal about stale candidate inputs cannot escape as current.
                capture.VerifyUnchanged();
                throw;
            }
            capture.VerifyUnchanged();
            cancellationToken.ThrowIfCancellationRequested();
            Console.Out.Write(rendered + Environment.NewLine);
            return ToolExitCode.Success;
        }, cancellationToken));
        return command;
    }

    private static async Task<int> Guarded(Func<Task<int>> action, CancellationToken cancellationToken)
    {
        try { return await action(); }
        catch (CliRefusal refusal)
        {
            Report.WriteRefusal(Console.Error, refusal.Code, refusal.Message, refusal.Details);
            return refusal.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report.WriteRefusal(Console.Error, "candidate-inspection-cancelled", "Candidate inspection was cancelled.", []);
            return ToolExitCode.Refusal;
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            Report.WriteRefusal(Console.Error, "candidate-inspection-failed", "Candidate inspection could not be completed.", []);
            return ToolExitCode.ResolutionFailure;
        }
    }

    private static bool IsNonFatal(Exception failure) => failure is not (OutOfMemoryException or StackOverflowException or
        AccessViolationException or AppDomainUnloadedException or CannotUnloadAppDomainException or ThreadAbortException or
        BadImageFormatException);
}
