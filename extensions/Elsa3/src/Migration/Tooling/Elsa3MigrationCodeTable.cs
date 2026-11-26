using System.Collections.Frozen;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Mapping.Services;
using Elsa3.Models;
using static Elsa3.Migration.Tooling.Elsa3MigrationComponentKind;
using static Elsa3.Migration.Tooling.Elsa3MigrationRemedy;

namespace Elsa3.Migration.Tooling;

/// <summary>
/// The code table of the migration report (spec 1237 FR-015, FR-016, FR-016a, FR-019): every code the converter, the
/// import, reconciliation and the tooling emit, mapped to a remedy, a component kind for component codes, a routine mark,
/// and, where the emitted text may carry input the converter does not redact, a fixed text. A code from Elsa 4 that the
/// table does not list gets <see cref="Elsa3MigrationRemedy.PatchDefinition"/> and <see cref="UnlistedText"/>.
/// <c>Elsa3MigrationCodeTableTests</c> fails when an Elsa 3 code has no entry.
/// </summary>
internal static class Elsa3MigrationCodeTable
{
    /// <summary>The report text of an Elsa 4 code the table does not list; its own message may quote script source.</summary>
    public const string UnlistedText = "Elsa 4 refused this definition while it was imported or published. The Elsa 4 message is not copied into the report because it can quote the definition's content.";

    /// <summary>The report guidance of an Elsa 4 code the table does not list.</summary>
    public const string UnlistedGuidance = "Look up the code in the Elsa 4 documentation, inspect the definition at the reported path, and change it, or report a converter gap.";

    private static readonly Elsa3MigrationCodeTableEntry Patch = new(PatchDefinition);
    private static readonly Elsa3MigrationCodeTableEntry NoEquivalent = new(None);
    private static readonly Elsa3MigrationCodeTableEntry Unlisted = new(PatchDefinition, FixedText: UnlistedText, FixedGuidance: UnlistedGuidance);

    private static readonly FrozenDictionary<string, Elsa3MigrationCodeTableEntry> Entries = Build().ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Gets every listed code, for the completeness test.</summary>
    public static IEnumerable<string> Codes => Entries.Keys;

    /// <summary>Whether the code has an entry of its own.</summary>
    public static bool Contains(string code) => Entries.ContainsKey(code);

    /// <summary>Gets the code's entry, or the default for an unlisted (Elsa 4) code.</summary>
    public static Elsa3MigrationCodeTableEntry Get(string code) => Entries.GetValueOrDefault(code, Unlisted);

    private static Dictionary<string, Elsa3MigrationCodeTableEntry> Build() => new(StringComparer.Ordinal)
    {
        // Models: Elsa3MigrationDiagnosticCodes.
        [Elsa3MigrationDiagnosticCodes.UnsupportedInputKind] = new(PatchDefinition,
            FixedText: "This input holds no Elsa 3 workflow definition that the migration can read.",
            FixedGuidance: "Export authored Elsa 3 workflow definitions (a definition or an array of them per file), or remove the file from the export."),
        [Elsa3MigrationDiagnosticCodes.LiveInstanceResumeUnsupported] = NoEquivalent,
        [Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed] = Patch,

        // Models: Elsa3ActivityDiagnosticCodes (ELS3-ACTIVITY-*, ELS3-LITERAL-*, ELS3-BEHAVIOUR-*).
        [Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous] = Patch,
        [Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten] = Patch,
        [Elsa3ActivityDiagnosticCodes.LiteralDateOffsetMissing] = Patch,
        [Elsa3ActivityDiagnosticCodes.LiteralDateInterpreted] = Patch,
        [Elsa3ActivityDiagnosticCodes.LiteralDateWallClock] = Patch,
        [Elsa3ActivityDiagnosticCodes.UnmappedType] = new(BuildComponent, Activity),
        [Elsa3ActivityDiagnosticCodes.AmbiguousAlias] = Patch,
        [Elsa3ActivityDiagnosticCodes.NoCounterpart] = NoEquivalent,
        [Elsa3ActivityDiagnosticCodes.VersionUnavailable] = Patch,
        [Elsa3ActivityDiagnosticCodes.StructureUnreadable] = Patch,
        [Elsa3ActivityDiagnosticCodes.UnknownMember] = Patch,
        [Elsa3ActivityDiagnosticCodes.MemberSemanticsChanged] = Patch,
        [Elsa3ActivityDiagnosticCodes.ValueOutsideContract] = Patch,
        [Elsa3ActivityDiagnosticCodes.VariableValueUnconvertible] = Patch,
        [Elsa3ActivityDiagnosticCodes.NullUnconvertible] = Patch,
        [Elsa3ActivityDiagnosticCodes.OutputNotRepresentable] = Patch,
        [Elsa3ActivityDiagnosticCodes.RemovedInputBound] = Patch,
        [Elsa3ActivityDiagnosticCodes.RequiredInputUnbound] = Patch,
        [Elsa3ActivityDiagnosticCodes.UnsupportedMode] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphConversionPending] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphDuplicateNodeId] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphConnectionInvalid] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphOutcomeUnmapped] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphJoinUnsupported] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphLoopUnsupported] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphStartAmbiguous] = Patch,
        [Elsa3ActivityDiagnosticCodes.GraphStartTriggerMisplaced] = Patch,
        [Elsa3ActivityDiagnosticCodes.ReusableIntrinsicUnsupported] = NoEquivalent,
        [Elsa3ActivityDiagnosticCodes.NestingTooDeep] = Patch,
        [Elsa3ActivityDiagnosticCodes.DuplicateInputBinding] = Patch,
        [Elsa3ActivityDiagnosticCodes.DispatchInputUndeclared] = Patch,
        // ELS3-BEHAVIOUR-* are Reconsider warnings; the remedy applies only if one is ever emitted at Error.
        [Elsa3ActivityDiagnosticCodes.FormPostNotParsed] = Patch,
        [Elsa3ActivityDiagnosticCodes.RequestSizeNoOutcome] = Patch,
        [Elsa3ActivityDiagnosticCodes.HttpOutcomeSingle] = Patch,
        [Elsa3ActivityDiagnosticCodes.DispatchInputNames] = Patch,
        [Elsa3ActivityDiagnosticCodes.AuthHeaderUnvalidated] = Patch,
        [Elsa3ActivityDiagnosticCodes.HttpAuthorizationUseConnection] = Patch,
        [Elsa3ActivityDiagnosticCodes.WhilePersistedCondition] = Patch,
        [Elsa3ActivityDiagnosticCodes.ForZeroStepFaults] = Patch,
        [Elsa3ActivityDiagnosticCodes.BreakStopsAtParallel] = Patch,
        [Elsa3ActivityDiagnosticCodes.ForkWaitAnyCancellation] = Patch,
        [Elsa3ActivityDiagnosticCodes.ExecuteWorkflowDurable] = Patch,
        [Elsa3ActivityDiagnosticCodes.NullTakesElsa4Default] = Patch,
        [Elsa3ActivityDiagnosticCodes.OutcomeUndeclared] = Patch,
        [Elsa3ActivityDiagnosticCodes.OutcomeUnreached] = Patch,
        [Elsa3ActivityDiagnosticCodes.HttpResponseJsonVariant] = Patch,
        [Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved] = Patch,
        // The null-policy record the converter adds to every definition: routine, so a definition can still go smoothly.
        [Elsa3ActivityDiagnosticCodes.NullPolicies] = new(PatchDefinition, Routine: true),
        [Elsa3ActivityDiagnosticCodes.InputDefaultRequired] = Patch,

        // Mapping: Elsa3ImportDiagnosticCodes (VF-IMP-*, ELS3-*).
        [Elsa3ImportDiagnosticCodes.DanglingReference] = Patch,
        [Elsa3ImportDiagnosticCodes.AmbiguousReference] = Patch,
        [Elsa3ImportDiagnosticCodes.CrossFrameReference] = Patch,
        [Elsa3ImportDiagnosticCodes.UnresolvableReferenceShape] = Patch,
        // C#, Python, unmatchable calls and forbidden capabilities: there is nothing to build, the script changes.
        [Elsa3ImportDiagnosticCodes.ForbiddenCapability] = Patch,
        [Elsa3ImportDiagnosticCodes.IdentityNormalization] = Patch,
        [Elsa3ImportDiagnosticCodes.DynamicAccessor] = Patch,
        [Elsa3ImportDiagnosticCodes.UnsupportedShape] = Patch,
        [Elsa3ImportDiagnosticCodes.IncompatibleValue] = Patch,
        [Elsa3ImportDiagnosticCodes.LiveInstanceState] = NoEquivalent,
        [Elsa3ImportDiagnosticCodes.ResolutionDiffers] = Patch,
        [Elsa3ImportDiagnosticCodes.ShadowedCandidate] = Patch,
        [Elsa3ImportDiagnosticCodes.DotNetMemberUsage] = Patch,
        [Elsa3ImportDiagnosticCodes.ToJsonRewritten] = Patch,
        [Elsa3ImportDiagnosticCodes.DateAssumedUtc] = Patch,
        [Elsa3ImportDiagnosticCodes.DateInterpretationApplied] = Patch,
        [Elsa3ImportDiagnosticCodes.ScriptOutcomeCatchAll] = Patch,
        [Elsa3ImportDiagnosticCodes.UntypedResultIntoTypedInput] = Patch,
        [Elsa3ImportDiagnosticCodes.SetterOfUndeclaredVariable] = Patch,
        [Elsa3ImportDiagnosticCodes.VariableTypeUnregistered] = new(BuildComponent, VariableType),
        [Elsa3ImportDiagnosticCodes.MemoryStoragePersisted] = new(BuildComponent, StorageDriver),
        [Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced] = new(BuildComponent, StorageDriver),
        // A host function already provides the name, so nothing is left to build (spec Q11).
        [Elsa3ImportDiagnosticCodes.ReplacedFunctionReference] = Patch,
        // A call or value read a registered prefixed host function may take over (FR-016a).
        [Elsa3ImportDiagnosticCodes.HostFunctionMissing] = new(BuildComponent, JavaScriptFunction),

        // Import: ReusableActivityImportDiagnosticCodes (ELS3-REUSABLE-*).
        [ReusableActivityImportDiagnosticCodes.DuplicateSourceVersion] = Patch,
        [ReusableActivityImportDiagnosticCodes.DuplicateNodeId] = Patch,
        [ReusableActivityImportDiagnosticCodes.InvalidSource] = Patch,
        [ReusableActivityImportDiagnosticCodes.MissingReference] = Patch,
        [ReusableActivityImportDiagnosticCodes.AmbiguousReference] = Patch,
        [ReusableActivityImportDiagnosticCodes.UnsupportedReference] = Patch,
        [ReusableActivityImportDiagnosticCodes.UnsupportedTrigger] = Patch,
        [ReusableActivityImportDiagnosticCodes.DependencyCycle] = Patch,
        [ReusableActivityImportDiagnosticCodes.PlanChanged] = Patch,
        [ReusableActivityImportDiagnosticCodes.SelectionNotClosed] = Patch,
        [ReusableActivityImportDiagnosticCodes.SelectionInvalid] = Patch,
        [ReusableActivityImportDiagnosticCodes.CollectionExpired] = Patch,
        [ReusableActivityImportDiagnosticCodes.IdempotencyConflict] = Patch,
        [ReusableActivityImportDiagnosticCodes.IdentityCollision] = Patch,
        [ReusableActivityImportDiagnosticCodes.UnsupportedStartTrigger] = Patch,
        [ReusableActivityImportDiagnosticCodes.PublishingUnavailable] = Patch,
        [ReusableActivityImportDiagnosticCodes.DependencyNotPublished] = Patch,

        // Reconciliation: Elsa3ReconciliationDiagnosticCodes (ELS3-RECONCILE-*). Skips are reported at Error. Their causes
        // can carry an Elsa 4 message, so the report writes a fixed text.
        [Elsa3ReconciliationDiagnosticCodes.DuplicateInput] = new(PatchDefinition,
            FixedText: "Another input carries the same source version id, or the same definition id and version.",
            FixedGuidance: "Keep one copy of the definition version in the export."),
        // A blocked definition is fixed by fixing its root, whose own finding carries the real remedy (data-model).
        [Elsa3ReconciliationDiagnosticCodes.BlockedByDependency] = new(PatchDefinition,
            FixedText: "A reusable definition this one uses was refused or not published, so this one was not imported.",
            FixedGuidance: "Fix the definitions named under blockedBy first, then run the migration again."),
        [Elsa3ReconciliationDiagnosticCodes.PublishFailed] = new(PatchDefinition,
            FixedText: "The imported workflow could not be published. Publishing stopped at its first error, so later publication errors may be hidden until this one is fixed.",
            FixedGuidance: "Fix the publication finding of this definition and run the migration again."),

        // The tooling's own codes (spec Q13).
        [Elsa3MigrationToolingCodes.InputUnreadable] = new(PatchDefinition,
            FixedText: "This input file is not valid JSON.",
            FixedGuidance: "Export the definition again, or remove the file from the export."),
        [Elsa3MigrationToolingCodes.NoPublishedVersion] = new(PatchDefinition,
            FixedText: "This definition has no published version, so its latest version was converted.",
            FixedGuidance: "Check that the latest version is the one to migrate."),
        [Elsa3MigrationToolingCodes.SupersededVersion] = new(PatchDefinition,
            FixedText: "A newer version of this definition was selected, so this version was imported for its findings only and is neither published nor exported.",
            FixedGuidance: "Nothing to do unless this version is the one to migrate."),
        [Elsa3MigrationToolingCodes.ImportRefused] = new(PatchDefinition,
            FixedText: "The import refused the selection as a whole, so no selected definition was applied.",
            FixedGuidance: "Inspect the definitions for credential literals and identity collisions, fix them, and run the migration again."),
        [Elsa3MigrationToolingCodes.RunRefused] = new(PatchDefinition,
            FixedText: "This definition was not applied because the import refused the run; see runFindings.",
            FixedGuidance: "Fix the run finding, then run the migration again."),
    };
}
