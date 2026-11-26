using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Mapping.Services;
using Elsa3.Migration.Tooling;
using Elsa3.Migration.Tooling.Report;
using Elsa3.Models;

namespace Elsa3.Migration.Tests;

public sealed class ReportBuilderTests
{
    [Theory]
    [InlineData("VF-IMP-005", "Error", ReportBuilder.MustFix)]
    [InlineData("ELS3-BEHAVIOUR-FOR-ZERO-STEP-FAULTS", "Error", ReportBuilder.MustFix)]
    [InlineData("ELS3-BEHAVIOUR-FOR-ZERO-STEP-FAULTS", "Warning", ReportBuilder.Reconsider)]
    [InlineData("ELS3-LITERAL-KEY-REWRITTEN", "Warning", ReportBuilder.TakeIntoAccount)]
    [InlineData("ELS3-BEHAVIOUR-FOR-ZERO-STEP-FAULTS", "Info", ReportBuilder.TakeIntoAccount)]
    [InlineData("ELSA4-UNKNOWN", "Info", ReportBuilder.TakeIntoAccount)]
    public void Category_follows_severity_and_the_behaviour_prefix(string code, string severity, string expected) =>
        Assert.Equal(expected, ReportBuilder.Categorize(code, Enum.Parse<FindingSeverity>(severity)));

    public static TheoryData<string[], string> BucketCases => new()
    {
        { [], ReportBuilder.ConvertAsIs },
        { ["info"], ReportBuilder.ConvertAsIs },
        { ["takeIntoAccount"], ReportBuilder.ConvertAsIs },
        { ["reconsider"], ReportBuilder.ConvertWithAdjustments },
        { ["mustFix:patch-definition"], ReportBuilder.ConvertWithAdjustments },
        { ["mustFix:build-component", "reconsider"], ReportBuilder.ConvertWithAdjustments },
        { ["mustFix:none"], ReportBuilder.Incompatible },
        { ["mustFix:patch-definition", "mustFix:none", "reconsider"], ReportBuilder.Incompatible },
    };

    [Theory]
    [MemberData(nameof(BucketCases))]
    public void Bucket_rule(string[] findings, string expected)
    {
        var report = findings.Select(item =>
        {
            var parts = item.Split(':');
            var category = parts[0] == "info" ? ReportBuilder.TakeIntoAccount : parts[0];
            return new ReportFinding("X", "error", category, parts.Length > 1 ? parts[1] : null, null, null, "conversion", null, null, "m", "g", null, null);
        }).ToArray();

        Assert.Equal(expected, ReportBuilder.Bucket(report));
    }

    [Fact]
    public void Remedy_is_written_for_must_fix_only_and_unlisted_codes_get_patch_definition_and_fixed_text()
    {
        var mustFix = ReportBuilder.Classify(Finding("ELSA4-PUBLISH-SOMETHING", FindingSeverity.Error, FindingStage.Publish, message: "quoted SENTINEL-1237 script"));
        var warning = ReportBuilder.Classify(Finding(Elsa3ActivityDiagnosticCodes.NoCounterpart, FindingSeverity.Warning));

        Assert.Equal("patch-definition", mustFix.Remedy);
        Assert.Equal(Elsa3MigrationCodeTable.UnlistedText, mustFix.Message);
        Assert.Equal(Elsa3MigrationCodeTable.UnlistedGuidance, mustFix.Guidance);
        Assert.DoesNotContain("SENTINEL-1237", mustFix.Message, StringComparison.Ordinal);
        Assert.Null(warning.Remedy);
    }

    [Fact]
    public void Converter_owned_codes_keep_their_redacted_message_and_guidance()
    {
        var finding = ReportBuilder.Classify(Finding(Elsa3ActivityDiagnosticCodes.NoCounterpart, FindingSeverity.Error, message: "No counterpart.", guidance: "Redesign it."));

        Assert.Equal("none", finding.Remedy);
        Assert.Equal("No counterpart.", finding.Message);
        Assert.Equal("Redesign it.", finding.Guidance);
    }

    [Fact]
    public void Component_codes_carry_kind_and_name_only_from_metadata()
    {
        var named = ReportBuilder.Classify(Finding(Elsa3ActivityDiagnosticCodes.UnmappedType, FindingSeverity.Error, componentName: "Acme.SendInvoice"));
        var unnamed = ReportBuilder.Classify(Finding(Elsa3ActivityDiagnosticCodes.UnmappedType, FindingSeverity.Error));
        var notComponent = ReportBuilder.Classify(Finding(Elsa3ActivityDiagnosticCodes.NoCounterpart, FindingSeverity.Error, componentName: "Ignored"));

        Assert.Equal(("activity", "Acme.SendInvoice", "build-component"), (named.ComponentKind, named.ComponentName, named.Remedy));
        Assert.Null(unnamed.ComponentKind);
        Assert.Null(notComponent.ComponentKind);
        Assert.Null(notComponent.ComponentName);
    }

    [Fact]
    public void Skip_codes_are_errors_with_fixed_text()
    {
        var skip = ReportBuilder.Classify(Finding(Elsa3ReconciliationDiagnosticCodes.PublishFailed, FindingSeverity.Error, FindingStage.Publish, message: "Elsa 4 said SENTINEL-1237"));

        Assert.Equal(("error", ReportBuilder.MustFix, "patch-definition"), (skip.Severity, skip.Category, skip.Remedy));
        Assert.DoesNotContain("SENTINEL-1237", skip.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string[], bool> SmoothCases => new()
    {
        { [], true },
        { [RoutineCode], true },
        { [RoutineCode, Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten], false },
        { [Elsa3ImportDiagnosticCodes.DateInterpretationApplied], false },
    };

    [Theory]
    [MemberData(nameof(SmoothCases))]
    public void Goes_smoothly_only_with_routine_findings(string[] codes, bool expected)
    {
        var report = Build(Definition("a", findings: codes.Select(code => Finding(code, FindingSeverity.Info)).ToArray()));

        Assert.Equal(expected, ReportBuilder.GoesSmoothly(report.Definitions.Single()));
        Assert.Equal(expected ? 1 : 0, report.Summary.GoesSmoothly);
    }

    [Fact]
    public void Blocked_definitions_name_the_root_through_any_chain()
    {
        var blocked = Finding(Elsa3ReconciliationDiagnosticCodes.BlockedByDependency, FindingSeverity.Error, FindingStage.Import);
        var report = Build(
            Definition("root", findings: [Finding(Elsa3ActivityDiagnosticCodes.NoCounterpart, FindingSeverity.Error)]),
            Definition("middle", blockedBy: ["root-v1"], findings: [blocked]),
            Definition("leaf", blockedBy: ["middle-v1"], findings: [blocked]),
            Definition("cycle-a", blockedBy: ["cycle-b-v1", "root-v1"], findings: [blocked]),
            Definition("cycle-b", blockedBy: ["cycle-a-v1"], findings: [blocked]));

        var byId = report.Definitions.ToDictionary(definition => definition.DefinitionId);
        Assert.Equal(["root-v1"], byId["leaf"].BlockedBy);
        Assert.Equal(["root-v1"], byId["middle"].BlockedBy);
        Assert.Equal(["root-v1"], byId["cycle-a"].BlockedBy);
        Assert.Empty(byId["root"].BlockedBy);
        Assert.Equal(ReportBuilder.ConvertWithAdjustments, byId["leaf"].Bucket);
        Assert.Equal(ReportBuilder.Incompatible, byId["root"].Bucket);
        Assert.Equal(4, report.Summary.BlockedByDependency);
    }

    [Fact]
    public void Component_inventory_counts_usages_and_lists_only_used_components()
    {
        var report = Build(
            Definition("b", findings:
            [
                Finding(Elsa3ActivityDiagnosticCodes.UnmappedType, FindingSeverity.Error, componentName: "Acme.SendInvoice", path: "$.a"),
                Finding(Elsa3ActivityDiagnosticCodes.UnmappedType, FindingSeverity.Error, componentName: "Acme.SendInvoice", path: "$.b"),
            ]),
            Definition("a", findings:
            [
                Finding(Elsa3ActivityDiagnosticCodes.UnmappedType, FindingSeverity.Error, componentName: "Acme.SendInvoice"),
                Finding(Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced, FindingSeverity.Warning, componentName: "Acme.BlobStorage"),
            ]),
            Definition("c"));

        Assert.Collection(report.Components,
            component => Assert.Equal(("activity", "Acme.SendInvoice", "a,b", 3), (component.Kind, component.Name, string.Join(",", component.Definitions), component.Usages)),
            component => Assert.Equal(("storage-driver", "Acme.BlobStorage", "a", 1), (component.Kind, component.Name, string.Join(",", component.Definitions), component.Usages)));
    }

    [Fact]
    public void Summary_exit_code_and_ordering()
    {
        var report = Build(
            Definition("b", version: 2, findings: [Finding(Elsa3ActivityDiagnosticCodes.FormPostNotParsed, FindingSeverity.Warning)]),
            Definition("b", version: 1, published: false, exportedTo: null),
            Definition("a", findings:
            [
                Finding("Z", FindingSeverity.Info, FindingStage.Publish),
                Finding("B", FindingSeverity.Info, path: "$.b"),
                Finding("A", FindingSeverity.Info, path: "$.b"),
                Finding("C", FindingSeverity.Info, path: "$.a"),
            ]));

        Assert.Equal([("a", 1), ("b", 1), ("b", 2)], report.Definitions.Select(definition => (definition.DefinitionId, definition.Version)));
        Assert.Equal(["C", "A", "B", "Z"], report.Definitions[0].Findings.Select(finding => finding.Code));
        Assert.Equal(1, report.ExitCode);
        Assert.Equal((0, 1, 4), (report.Summary.ByCategory.MustFix, report.Summary.ByCategory.Reconsider, report.Summary.ByCategory.TakeIntoAccount));
        Assert.Equal((2, 1, 0), (report.Summary.ByBucket.ConvertAsIs, report.Summary.ByBucket.ConvertWithAdjustments, report.Summary.ByBucket.Incompatible));
        Assert.Equal((3, 2, 2), (report.Summary.Definitions, report.Summary.Published, report.Summary.Exported));
        Assert.True(report.Redacted);
    }

    [Fact]
    public void Exit_code_is_zero_without_must_fix_or_reconsider()
    {
        var report = Build(Definition("a", findings: [Finding(Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten, FindingSeverity.Warning)]));

        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void Run_and_input_findings_count_toward_the_exit_code()
    {
        var input = Input([Definition("a")]) with
        {
            InputFindings = [new CollectedInputFinding("bad.json", Finding(Elsa3MigrationToolingCodes.InputUnreadable, FindingSeverity.Error, FindingStage.Input))]
        };

        var report = ReportBuilder.Build(input);

        Assert.Equal(1, report.ExitCode);
        Assert.Equal("This input file is not valid JSON.", report.InputFindings.Single().Finding.Message);
    }

    internal const string RoutineCode = Elsa3ActivityDiagnosticCodes.NullPolicies;

    internal static CollectedFinding Finding(
        string code,
        FindingSeverity severity,
        FindingStage stage = FindingStage.Conversion,
        string? path = "$.root",
        string message = "message",
        string? guidance = "guidance",
        string? componentName = null) =>
        new(code, severity, stage, path, null, message, guidance, componentName);

    internal static CollectedDefinition Definition(
        string id,
        int version = 1,
        bool published = true,
        string? exportedTo = "migrated/workflows/x.json",
        string[]? blockedBy = null,
        CollectedFinding[]? findings = null) =>
        new(id, id, version, $"{id}-v{version}", false, published, exportedTo, null, blockedBy ?? [], findings ?? []);

    internal static ReportInput Input(CollectedDefinition[] definitions) =>
        new("inventory", "1.0.0-test", false, definitions, [], [], [], [], ["Elsa3Mapping"], ["CShells:Shells:default:Features:Elsa3Mapping"], true);

    private static MigrationReport Build(params CollectedDefinition[] definitions) => ReportBuilder.Build(Input(definitions));
}
