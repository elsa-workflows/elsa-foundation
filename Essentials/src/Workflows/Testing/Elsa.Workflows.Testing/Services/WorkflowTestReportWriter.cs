using System.Globalization;
using System.Text;
using System.Xml;
using Elsa.Workflows.Testing.Core.Contracts;
using Elsa.Workflows.Testing.Core.Models;

namespace Elsa.Workflows.Testing.Services;

internal sealed class WorkflowTestReportWriter : IWorkflowTestReportWriter
{
    private readonly IWorkflowTestReportPolicy _policy;

    public WorkflowTestReportWriter(IEnumerable<IWorkflowTestReportPolicy> policies)
    {
        var providers = policies.ToArray();
        _policy = providers.Length == 1 ? providers[0] : throw new InvalidOperationException("Workflow test reports require exactly one report policy provider.");
    }

    public void WriteJunit(TestRunReport report, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);
        var exitCode = _policy.GetExitCode(report);
        var aggregateError = exitCode != 0 && !report.Cases.Any(x => Kind(x) is "failure" or "error");
        using var xml = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, CloseOutput = false });
        xml.WriteStartDocument();
        xml.WriteStartElement("testsuite");
        xml.WriteAttributeString("name", Safe(report.RunId));
        xml.WriteAttributeString("tests", (report.Cases.Count + (aggregateError ? 1 : 0)).ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("failures", report.Cases.Count(x => Kind(x) == "failure").ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("errors", (report.Cases.Count(x => Kind(x) == "error") + (aggregateError ? 1 : 0)).ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("skipped", report.Cases.Count(x => Kind(x) == "skipped").ToString(CultureInfo.InvariantCulture));
        xml.WriteStartElement("properties");
        Property("profile", TestRunReport.Profile);
        Property("runId", report.RunId);
        Property("lifecycle", report.Lifecycle.ToString());
        Property("requestFingerprint", report.RequestFingerprint);
        Property("materialFingerprint", report.MaterialFingerprint);
        Property("observationSequence", report.ObservationSequence.ToString(CultureInfo.InvariantCulture));
        Property("exitCode", exitCode.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement();
        foreach (var item in report.Cases)
        {
            xml.WriteStartElement("testcase");
            xml.WriteAttributeString("name", Safe(item.SelectionKey));
            xml.WriteAttributeString("classname", Safe(item.Candidate.DefinitionId));
            xml.WriteStartElement("properties");
            Property("attemptId", item.AttemptId);
            Property("selectionKey", item.SelectionKey);
            Property("caseId", item.Case.ResourceId);
            Property("caseRevision", item.Case.RevisionId);
            Property("caseFingerprint", item.Case.Fingerprint);
            Property("artifactId", item.Candidate.ArtifactId);
            Property("artifactFingerprint", item.Candidate.ArtifactFingerprint);
            Property("sourceFingerprint", item.Candidate.SourceFingerprint);
            Property("targetModel", item.Target.ModelProfile);
            Property("targetKind", item.Target.Kind.ToString());
            if (item.Target.NodeId is { } nodeId) Property("targetNode", nodeId);
            Property("mode", item.Mode.ToString());
            Property("timePolicy", item.Time.Kind.ToString());
            Property("initialInstant", item.Time.InitialInstant.ToString("O", CultureInfo.InvariantCulture));
            Property("verdict", item.Verdict.ToString());
            Property("executionStatus", item.Execution.Status);
            Property("cleanupSettled", item.Closure.CleanupSettled.ToString());
            foreach (var dependency in item.Candidate.Dependencies)
                Property("dependency:" + dependency.Key, dependency.Profile + ":" + dependency.Revision + ":" + dependency.Fingerprint);
            xml.WriteEndElement();
            if (Kind(item) is { } kind)
            {
                xml.WriteStartElement(kind);
                xml.WriteAttributeString("type", item.Verdict.ToString());
                xml.WriteAttributeString("message", Safe(item.SkipReason ?? AllChecks(item).FirstOrDefault(x => x.Verdict != TestCheckVerdict.Passed)?.Message ?? item.Verdict.ToString()));
                foreach (var check in AllChecks(item).Where(x => x.Verdict != TestCheckVerdict.Passed))
                    xml.WriteString(Safe(check.Key + ": " + check.Message + "\n"));
                foreach (var diagnostic in item.Diagnostics)
                    xml.WriteString(Safe(diagnostic.Code + ": " + diagnostic.Message + "\n"));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        if (aggregateError)
        {
            xml.WriteStartElement("testcase");
            xml.WriteAttributeString("name", "Required test selection");
            xml.WriteAttributeString("classname", "Elsa.WorkflowTests.Gate");
            xml.WriteStartElement("error");
            xml.WriteAttributeString("type", "RunGate");
            xml.WriteAttributeString("message", $"The run did not satisfy its required selection/settlement policy (exit {exitCode}).");
            xml.WriteEndElement();
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndDocument();
        xml.Flush();

        string? Kind(TestCaseResult item)
        {
            var selection = WorkflowTestReportPolicy.SelectionFor(report, item);
            return report.Lifecycle != TestRunLifecycle.Finalized || !WorkflowTestReportPolicy.HasSettledCleanup(item) || selection is null ? "error" : item.Verdict switch
            {
                TestVerdict.Skipped when WorkflowTestReportPolicy.HasAuthorizedSkip(report, item, selection) => "skipped",
                TestVerdict.Cancelled => "skipped",
                TestVerdict.Passed when WorkflowTestReportPolicy.HasAuthoritativeChecks(item, selection.Plan) => null,
                TestVerdict.Failed when WorkflowTestReportPolicy.HasAuthoritativeChecks(item, selection.Plan) => "failure",
                _ => "error"
            };
        }

        void Property(string name, string value)
        {
            xml.WriteStartElement("property");
            xml.WriteAttributeString("name", Safe(name));
            xml.WriteAttributeString("value", Safe(value));
            xml.WriteEndElement();
        }
    }

    private static IEnumerable<TestCheckResult> AllChecks(TestCaseResult item) => item.Checks.Concat(item.Steps.SelectMany(x => x.Checks));

    private static string Safe(string value)
    {
        var output = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var c = rune.Value;
            output.Append(c is 9 or 10 or 13 || c is >= 0x20 and <= 0xd7ff || c is >= 0xe000 and <= 0xfffd || c is >= 0x10000 and <= 0x10ffff
                ? rune.ToString() : "\uFFFD");
        }
        return output.ToString();
    }
}
