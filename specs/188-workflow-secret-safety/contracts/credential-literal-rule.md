# Contract: the credential-literal rule

Proposed shapes for FR-008 and FR-009. Decisions and alternatives are in [research R7](../research.md).

## Identity

- Rule identifier: `Inputs/CredentialLiteral` (the R3 validation-category form `ValidationError.Type` already uses).
- Finding: `ValidationError(Path: "{nodeId}/inputs/{referenceKey}", Type: "Inputs/CredentialLiteral",
  Message: "Inputs/CredentialLiteral: input '{inputName}' on activity '{nodeId}' holds a credential and accepts only a secret reference.")`.
  The message starts with the rule identifier so every surface that shows only messages (including the existing
  promotion-gate translation) still carries it.
- The message and every refusal never echo the bound value, its expression text, or its length.

## Acceptance predicate (`Elsa.Workflows.Design.Core`)

For an activity input whose `InputDefinition.IsCredential == true` (on the pinned-contract path,
`ActivityInputContract.IsCredential == true`), the bound `ArgumentState` is accepted only when a row below says yes. The
credential flag is always read explicitly; `RequiresEncryption` is never used to infer it ([research R5](../research.md)):

| Binding | Accepted |
|---|---|
| no `ArgumentState`, or `Value` null | yes (unbound) |
| `Value.Value` null, empty string, JSON null, JSON undefined, JSON empty string | yes (unbound; same definition as `RequiredInputOutputValidator`) |
| `ExpressionType == "Secret"` (ordinal, ignore case, as the compiler already compares types) with a well-formed secret reference: a JSON object with a non-blank text `name`, an optional text `typeName` and an optional text `scope` (a null member reads as absent), no other member and no member twice, member names compared ordinally (`SecretReferencePayload`, `Elsa.Workflows.Design.Core`) | yes |
| `Secret` whose payload carries no value (null, JSON null or undefined, empty string: the syntax was chosen and nothing picked) | yes (no value is involved; publication refuses it, because there is no reference to compile) |
| `Secret` with any other payload: text, a number, an array, an object without a `name` or with a blank or non-text one, a non-text `typeName` or `scope`, any other member, a member twice (review round 3: the rule accepted every `Secret` binding before) | no |
| `Literal`, `Object`, `Default` (a `Default` request carries no value of its own but binds the declared default, a literal, so it is refused even with a null value, as publication treats it) | no |
| `Variable`, `WorkflowRequest`, `ActivityResult` | no |
| any text expression (`JavaScript`, `Liquid`, ...) | no |

A member outside `name`, `typeName` and `scope` is refused rather than ignored: a stored definition would carry it, and
an extra member is a place a value could ride. These are exactly the members the secret reference models declare
(`Elsa.Secrets.Core.Models.SecretReference`, `RuntimeSecretReference`) and the Studio secret picker writes
(`{ name, typeName, scope? }`). `SecretReferencePayload` is the one definition of a well-formed reference: publication
reads every secret reference through it, on any input, so a payload the rule refuses at save is refused at publish too.

Inputs that are not credentials are unaffected by this rule, including sensitive ones (FR-009). A non-credential
input whose effective policy requires encryption is governed by the separate publish rule `VF-ACT-011`
([research R8](../research.md)). The `"Secret"` literal is
duplicated from `Elsa.Secrets.Core.Models.SecretExpressionTypes.Secret` rather than referenced (framework §2.17);
a test in the bridge test project, which references both, pins equality.

## Enforcement and refusal shape at each entry point

The rule runs in the application layer only. Every application-layer writer of workflow state the coverage guard below
finds takes the rule's contract, `ICredentialLiteralValidator` (`Elsa.Workflows.Design.Validations.Core`). A caller that refuses a whole
request admits through the shared helper `WorkflowStateAdmission.AdmitAsync(validator, state)`, which throws
`CredentialLiteralRefusedException`; a per-item caller reads the same findings from `ICredentialLiteralValidator.Validate`
and skips the item; the Elsa 3 collection import reads the findings for every node it maps and throws one
`CredentialLiteralRefusedException` holding all of them. As built in slice 6, `WorkflowStateAdmission` is a static helper over the contract, like
`DraftValidationGate` beside it, rather than a sealed class with an injected validator: the architecture suite's
`.Core` shape ratchet (`Core_projects_contain_no_implementation_shaped_types`) admits no new class with injected
dependencies in a `.Core` project. It still holds no rule and cannot be replaced. No design persistence command and no
persistence feature changes; [research R7](../research.md) records why the guarded-writer and decorator options were
rejected.

| # | Entry point | Where the rule runs (integration point) | Refusal | HTTP (where an endpoint exists) |
|---|---|---|---|---|
| 1 | Draft save: Definitions/Add, Drafts/Replace, Definitions/Update (a mediator command; it has no HTTP route) | Design API admission: the endpoint or handler calls `WorkflowStateAdmission.AdmitAsync` on the incoming state before `IAddWorkflowDefinitionCommand` or `IUpdateDraftCommand` | `CredentialLiteralRefusedException`; the command never runs, nothing is stored | 400, errors keyed by path |
| 2 | Promote (Drafts/Promote) | Design API admission: the endpoint reads the stored draft through `IWorkflowDefinitionDraftStore`, calls `WorkflowStateAdmission.AdmitAsync` on its state, and passes the SHA-256 of the admitted draft's `StateSource` to `IPromoteDraftToVersionCommand` as the required expected-state hash. The command compares it with the draft it reads in-lock and promotes only that content. It does not depend on the command's optional in-lock gate, which runs only when `IInlineEventPublisher` is composed. When the draft no longer exists there is nothing to admit: the endpoint passes the hash of empty content and leaves the answer to the command (see "Promote's content precondition") | `CredentialLiteralRefusedException`; the command never runs, no version row written. A draft changed after admission: `WorkflowDraftChangedException`, no version row written | 400, errors keyed by path; 409 when the draft changed after admission |
| 3 | Publish (including publish-on-reconcile and draft test runs) | `RuntimeInputBindingCompiler.CompileAll`, per input | `CredentialLiteralRefusedException`, surfaced through publication's existing compile-error translation | 400 |
| 4 | Add version (Versions/Add) | Design API admission before `IAddWorkflowDefinitionVersionCommand` | same as row 1 | 400 |
| 5 | Submit (Definitions/Submit) | Design API admission before `ISubmitWorkflowDefinitionCommand` | same as row 1 | 400 |
| 6 | File-based reconciliation import (and git import, which feeds it) | `WorkflowsVersionReconciler.ReconcileVersion`, per item, before any catalog mutation for that item | that item only is refused; see "Per-item behavior" below | n/a |
| 7 | Git export | `GitWorkflowExporter`, per version not yet committed, before writing its file | that version file only is skipped; see "Per-item behavior" below | n/a |
| 8 | Elsa 3 collection import (`POST migration/elsa3/reusable-activities/collections/{collectionHandle}/apply`; admitted in slice 6's review) | `ReusableActivityCollectionImporter.ApplyAsync`, the import's application-layer service, after mapping and before its commit port (`IReusableActivityImportCommand`) runs: every activity node of each imported workflow version's state and of each reusable activity's mapped body (from which the materializer builds that activity version's descriptor payload), the root and every node nested under it at any depth. The mapping nests children under `elsa3.imported-activity.structure`, which no handler projects, so the import enumerates them itself (`Elsa3ImportedActivityStructure.Nodes`) and judges each node through `ICredentialLiteralValidator` (review round 2) | the apply is all or nothing, so the whole apply is refused with one `CredentialLiteralRefusedException` naming every refused binding, and nothing is committed | 400 through the import's existing problem ladder (`elsa3.import.request-invalid`, its `ArgumentException` arm), the findings' messages in `detail`; that problem body has no `errors` map |

Each refusal carries the rule identifier, the activity (node) id and the input name. Entry point 8 is not one of
FR-008's seven: it was found in slice 6 and admitted in its review, as file reconciliation is (spec FR-008 note).

A draft changed between promote's admission and the promotion lock is refused by the command's expected-state
comparison (409, `WorkflowDraftChangedException`), so promote never writes content its admission did not see; that comparison is a
storage-integrity compare-and-set and holds no rule ([research R7](../research.md)). Where the publisher is composed,
`CredentialLiteralValidator`'s registration as an `IDraftValidator` also lets the in-lock gate re-run the validators
(409, the existing gate shape); that is defense in depth, not the enforcement. The Design API's 400 problem body
follows the existing design translator shape (`WorkflowDesignExceptionTranslator`): `errors` keyed by
`{nodeId}/inputs/{referenceKey}`, each message starting with `Inputs/CredentialLiteral`.

## Blocking at draft save

This is the one deliberate exception to "draft save records validation errors without blocking" (spec
clarification). The exception is implemented by the Design API admission throwing before the command runs, not by
changing `DraftValidationGate`. The same validator also contributes its findings to `DraftValidating`, where they are
recorded like any other finding (and block at promote only where the promotion command's optional in-lock gate
runs; promote's own admission blocks regardless).

## Per-item behavior during file reconciliation and git export

Both run as startup tasks (`WorkflowsVersionReconcilerStartupTask`, `[Order(2)]`; `GitWorkflowExportStartupTask`,
`[Order(3)]`). An exception thrown out of either aborts the whole pass and keeps the host from becoming ready, and
the reconciler materializes the definition record before the version (verified in
`WorkflowsVersionReconciler.ReconcileVersion`). So a refusal there must never throw out of the pass:

- **Validate first.** The reconciler validates an item's state before it materializes anything for that item: no
  definition record, no metadata update, no version row. The exporter validates a version before it creates the
  version directory or writes the file.
- **Refuse that item only.** A refused reconciliation item is treated like an outdated one: it is not materialized
  and its provenance claim does not travel to `WorkflowVersionsReconciled`, so publish-on-reconcile never sees it. A
  refused export version is not written, committed or tagged.
- **Only versions not yet exported.** The exporter judges a version only when its file is not already committed: a
  committed file is immutable and is skipped before the rule runs, so a version exported before the rule existed (or
  before its activity was installed) stays in git history as it was written.
- **Value-free diagnostic.** Each refusal logs one warning naming the rule id, the definition id, the version, the
  node id and the input name, never the value, its length or its expression text.
- **Continue the pass.** The pass moves on to the next item. A pass whose only problems are refusals completes
  normally, publishes `WorkflowVersionsReconciled` for the items it did reconcile, and does not block host
  readiness. Export pushes whatever it committed.

## Promote's content precondition

The draft row has no revision or concurrency token, so the precondition is a content hash: the SHA-256 of the
draft's stored `StateSource` (empty when null), computed by the endpoint from the draft it admitted, through
`WorkflowDraftStateHash.Compute` (`Elsa.Workflows.Design.Persistence.Core`), which the command uses for its in-lock
read as well. When the endpoint finds no draft it admits nothing and passes `WorkflowDraftStateHash.Absent`, the hash
of empty content.

- **Required.** `IPromoteDraftToVersionCommand` has one method, `Execute(key, draftId, requestedVersion,
  expectedStateHash, ct)`, with a non-nullable `expectedStateHash`. The overloads without it are removed (their only
  `src/` caller, the Drafts/Promote endpoint, passes the hash; Elsa 4 is unreleased, so no compatibility overload).
  The command throws `ArgumentException` for a null or blank hash before it reads or writes anything.
- **Compared in-lock.** The comparison happens in the atomic-write delegate after the draft and definition locks are
  held and before any row is added; a difference throws `WorkflowDraftChangedException` (409).
- **Not part of the idempotency material.** `PromoteDraftRequestMaterial` stays draft id and version request only. A
  replay of an already-succeeded promote with the same operation key returns the original version id and writes
  nothing, even when the draft changed afterwards and the replay carries a different hash: the atomic writer resolves
  the operation marker before it takes the locks or runs the delegate. This loses no safety, because a replay writes
  nothing. Through the endpoint, a replay whose draft still exists is admitted first, so a replay after an edit that
  put a credential literal into the draft is refused with 400 before the command runs. A replay whose draft was
  discarded has nothing to admit: the endpoint passes `WorkflowDraftStateHash.Absent`, and the command's marker lookup
  returns the original version id. A first promotion of a missing draft finds no marker and is refused by the
  command's own draft lookup (404), as is one whose draft was deleted after the endpoint admitted it. A draft that
  appears after the endpoint found none is not promoted either: one whose stored `StateSource` is null or empty matches
  `Absent`, but the EF command cannot read it into a state (`EfDesignSupport.ReadState` refuses a missing state source)
  and throws before any row is added; any other draft differs from `Absent` and is refused as changed (409). Pinned at
  the command level in `EfWorkflowDesignPersistenceTests`.

## Composition

`ICredentialLiteralValidator` is a required constructor dependency of the six Design API callers (Definitions/Add,
Drafts/Replace, Definitions/Update, Versions/Add, Definitions/Submit, Drafts/Promote), `WorkflowsVersionReconciler`,
`GitWorkflowExporter` and the Elsa 3 import's `ReusableActivityCollectionImporter`. It is a §2.6.2 replacement
contract, declared by `[CredentialLiteralValidatorReplacementContract]`: `WorkflowDesignValidations` registers the
default once, also as an `IDraftValidator` for reporting, and a host that composes a second one fails shell activation
with `MultipleCredentialLiteralValidatorsException`, naming every registration. `WorkflowsDesignApi`,
`JsonWorkflowReconciliation`, `WorkflowsDesignGitReconciliation` (the two concrete features deriving from
`WorkflowsDesignReconciliationFeature`) and `Elsa3ImportJsonActivities` declare `DependsOn` on
`WorkflowDesignValidations`, so a host that composes them without the rule fails at composition instead of silently
skipping it. The persistence features declare nothing new.

## Coverage guard

An architecture test (T055) keeps the seam from being bypassed:

1. **Contract inventory.** Every public `*Command` interface in `Elsa.Workflows.Design.Persistence.Core.Contracts`
   must be classified on the guard's list as *state-writing, admitted* (today: `IAddWorkflowDefinitionCommand`,
   `IUpdateDraftCommand`, `IAddWorkflowDefinitionVersionCommand`, `ISubmitWorkflowDefinitionCommand`,
   `IPromoteDraftToVersionCommand`, `IMaterializeWorkflowDefinitionVersionCommand`, `ICreateDraftCommand`),
   *state-writing, exempt with a reason* (today only `ICloneDraftFromVersionCommand`, which copies a stored version),
   or *not state-writing* (`ISaveWorkflowDefinitionCommand`, `IMaterializeWorkflowDefinitionCommand`,
   `IDiscardDraftCommand`, `IDeleteWorkflowDefinitionPermanentlyCommand`). A new command fails the guard until
   someone classifies it.
2. **Caller coverage, the one assertion.** A source scan of `src/` finds every type outside the design persistence
   project (`src/essentials/Workflows/Design/Persistence/`, which implements the commands) whose constructor takes an
   admitted command, and asserts that the same constructor takes `ICredentialLiteralValidator`.
   The callers found must be exactly the seven known ones (the six Design API callers and `WorkflowsVersionReconciler`),
   so the scan cannot pass by scanning nothing and a new caller, admitted or not, turns it red until it is listed. An
   admitted command named anywhere else outside the design persistence project (service location, a method parameter,
   a field) is a violation too. Until slice 6's review the scan skipped every path containing `/Persistence/`, so a
   caller under another module's persistence folder went unseen.
3. **Git export.** The exporter calls no design command, so it is listed by name: every `IGitWorkflowExporter`
   implementation takes `ICredentialLiteralValidator`.
4. **Promote carries the admitted hash.** Every `Execute` method of `IPromoteDraftToVersionCommand` takes a
   non-nullable `string expectedStateHash` (reflection with `NullabilityInfoContext`), so a promote overload
   without it cannot come back.
5. **Writers that bypass the commands** (added in slice 6). A persistence-layer writer can write workflow state
   without any design command, through the design EF context or an EF command class constructed directly, which the
   constructor scan cannot see. Every production file outside the design persistence project that reaches the design EF
   context or a design EF command must be on the guard's list with what it does: today composition and read-only
   users, the activity upgrade (`EfActivityUpgradePlanStore`, which re-points stored nodes to another activity version
   and adds no authored content) and the Elsa 3 collection import's commit (`EfReusableActivityImportCommand`,
   admitted by its caller, assertion 6). A new one fails the guard until it is classified.
6. **The Elsa 3 collection import's commit port** (added in slice 6's review). The import stores workflow versions
   through `IReusableActivityImportCommand`, not a design command, so assertion 2 cannot see it: every constructor
   that takes the port takes `ICredentialLiteralValidator`, and the callers found are exactly the known one,
   `ReusableActivityCollectionImporter`.

Mutations that must turn it red: remove `ICredentialLiteralValidator` from one Design API caller (the promote endpoint
among them); add a new admitted-command caller without it, including one under another module's `/Persistence/`
folder; add a new `*Command` contract without classifying it; add an `Execute` overload to
`IPromoteDraftToVersionCommand` without `expectedStateHash`; remove `ICredentialLiteralValidator` from
`ReusableActivityCollectionImporter`.
Whether a caller actually calls the helper before the command is proved per entry point by T051 to T054, whose
bite-proofs remove the call.

## Known gaps (not covered by this rule)

- **Activities not in the catalog** (spec FR-008 and its edge case). The validator, like
  `RequiredInputOutputValidator`, skips nodes whose activity version the catalog cannot resolve, so it cannot tell
  whether their inputs are credentials. Draft save, add-version, submit, file reconciliation and git export
  cannot judge such nodes and accept them, and so does the Elsa 3 collection import. Promote accepts one only in a host
  with no publisher composed; in a standard host `UnknownActivityVersionValidator` reports the uncataloged node and
  promote answers 409 (the gate's `DraftHasValidationErrorsException` pinned over the real EF store by
  `CredentialLiteralPromoteAdmissionTests`, its 409 and problem shape over HTTP by `CredentialLiteralEndpointTests`). A
  draft or version holding such a literal can therefore be stored and
  exported: storage does not stop it, and what keeps it from running is publish, which cannot compile an activity
  version the catalog does not hold and, once the activity is installed, applies the rule. Proved by T112
  (A22).
- **Runtime artifact import** carries compiled bindings and is not one of the admitted entry points (research R13); the
  spec states it as out of scope for phase 0, with a follow-up (T090). Where the imported artifact's own policy marks
  an input as requiring encryption, R8's producer withholding, the `VF-ACT-010` activation refusal and the commit
  backstop keep such a value out of persisted runtime state; an artifact that does not mark it is not caught, and the
  literal stays in the imported file.
- **Children under a structure kind with no registered handler.** The rule's tree walk reaches only the children a
  registered structure handler projects: `DefaultActivityStructureService.ProjectChildren` returns none for a kind
  without one. So a credential literal on such a child is not judged at draft save, add-version, submit, promote, file
  reconciliation or git export. Publication does not close the gap in general:
  `ExecutableNodeCompiler.EnsureDeclaredStructureHasHandler` refuses the parent node, the one carrying the structure,
  only when its activity's catalog design facets declare that structure kind (the feature providing the activity is
  missing from the shell). Otherwise the
  compiler treats the structure as opaque (spec 071 FR-008): it compiles none of its children into executable nodes,
  so none is judged, and it carries the payload, the children's bindings included, into the compiled executable; the
  definition is stored and exported with them. Once a handler for the kind is installed, the walk and
  the compiler reach the children and the rule applies to them. Since slice 6's review round 2 the Elsa 3 collection
  import does not depend on that walk: it enumerates the children its mapping nests under
  `elsa3.imported-activity.structure` and judges each like any other node (entry point 8). A hand-authored state can
  still store such a child: draft save, add-version and submit through the Design API, and a workflow file read by
  file reconciliation or git import, accept a structure of any kind. A follow-up is recorded in T090.
- **The Elsa 3 upload itself.** The mapping keeps an Elsa 3 property only when its name matches a declared input or
  output; it stores an input binding under the declared input's reference key, whatever casing the Elsa 3 property
  name has, so the rule sees it (slice 6's review round 2). A property that matches no declared input or output is
  dropped from the mapped state, so no stored workflow state holds it and the rule has nothing to judge. The uploaded
  collection, every property included, is stored verbatim in the import ledger (`Elsa3ImportCollectionRecord.ContentJson`)
  when it is uploaded, before any analysis or apply, and nothing deletes it: its expiry only refuses later reads (410).
  The rule never applies to that source document, so a credential literal in an upload stays in the ledger whether the
  apply is refused or not. Found in slice 6's review round 2; a follow-up is recorded in T090.
- **A `Secret` binding with no payload.** A `Secret` binding whose payload carries no value (the syntax chosen and
  nothing picked yet) passes the rule at save, because drafts may be incomplete and no value is involved. Publication
  refuses it: the secret-reference reader finds no object reference payload. Every other malformed payload under the
  `Secret` type is refused by the rule itself, at every entry point (review round 3).
- **Publish reports expression diagnostics first.** Publication validates expressions before it compiles, so an
  invalid JavaScript binding on a credential input is answered with the expression-validation problem (422), not the
  rule's refusal; a valid one reaches the compiler and is refused by the rule.
- **An activity upgrade** re-points stored drafts (and drafts cloned from versions) to another activity version and
  writes them directly, adding no authored content. A literal that was admissible on the old version and sits on an
  input the new version declares a credential stays stored; promotion and publication refuse it.
- **Nesting beyond `MaxRecursionDepth`.** The validator walks the activity tree as the other validators do, to the
  configured depth (default 100); a credential literal nested deeper is not judged by the admission paths and is
  refused at publication, whose walk has no depth bound (for the children registered handlers project; see above).
