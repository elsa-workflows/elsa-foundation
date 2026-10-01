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
| `ExpressionType == "Secret"` (ordinal, ignore case, as the compiler already compares types) | yes |
| `Literal`, `Object`, `Default` | no |
| `Variable`, `WorkflowRequest`, `ActivityResult` | no |
| any text expression (`JavaScript`, `Liquid`, ...) | no |

Inputs that are not credentials are unaffected by this rule, including sensitive ones (FR-009). A non-credential
input whose effective policy requires encryption is governed by the separate publish rule `VF-ACT-011`
([research R8](../research.md)). The `"Secret"` literal is
duplicated from `Elsa.Secrets.Core.Models.SecretExpressionTypes.Secret` rather than referenced (framework §2.17);
a test in the bridge test project, which references both, pins equality.

## Enforcement and refusal shape at each entry point

The rule runs in the application layer only, and every application-layer caller runs it through one shared helper,
`WorkflowStateAdmission` (`Elsa.Workflows.Design.Validations.Core`): `AdmitAsync(state)` throws
`CredentialLiteralRefusedException`; `FindRefusalsAsync(state)` returns the same findings for per-item callers. No
design persistence command and no persistence feature changes; [research R7](../research.md) records why the
guarded-writer and decorator options were rejected.

| # | Entry point | Where the rule runs (integration point) | Refusal | HTTP (where an endpoint exists) |
|---|---|---|---|---|
| 1 | Draft save: Definitions/Add, Drafts/Replace, Definitions/Update | Design API admission: the endpoint or handler calls `WorkflowStateAdmission.AdmitAsync` on the incoming state before `IAddWorkflowDefinitionCommand` or `IUpdateDraftCommand` | `CredentialLiteralRefusedException`; the command never runs, nothing is stored | 400, errors keyed by path |
| 2 | Promote (Drafts/Promote) | Design API admission: the endpoint reads the stored draft through `IWorkflowDefinitionDraftStore` and calls `WorkflowStateAdmission.AdmitAsync` on its state before `IPromoteDraftToVersionCommand`. It does not depend on the command's optional in-lock gate, which runs only when `IInlineEventPublisher` is composed | `CredentialLiteralRefusedException`; the command never runs, no version row written | 400, errors keyed by path |
| 3 | Publish (including publish-on-reconcile and draft test runs) | `RuntimeInputBindingCompiler.CompileAll`, per input | `CredentialLiteralRefusedException`, surfaced through publication's existing compile-error translation | 400 |
| 4 | Add version (Versions/Add) | Design API admission before `IAddWorkflowDefinitionVersionCommand` | same as row 1 | 400 |
| 5 | Submit (Definitions/Submit) | Design API admission before `ISubmitWorkflowDefinitionCommand` | same as row 1 | 400 |
| 6 | File-based reconciliation import (and git import, which feeds it) | `WorkflowsVersionReconciler.ReconcileVersion`, per item, before any catalog mutation for that item | that item only is refused; see "Per-item behavior" below | n/a |
| 7 | Git export | `GitWorkflowExporter`, per version, before writing its file | that version file only is skipped; see "Per-item behavior" below | n/a |

Each refusal carries the rule identifier, the activity (node) id and the input name. Where the publisher is composed,
`CredentialLiteralValidator`'s registration as an `IDraftValidator` also lets the promotion command's in-lock gate
refuse (409, the existing gate shape) a draft changed between admission and the lock; that is defense in depth, not
the enforcement. The 400 problem body follows
the existing design translator shape (`WorkflowDesignExceptionTranslator.Validation`): `errors` keyed by
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
- **Value-free diagnostic.** Each refusal logs one warning naming the rule id, the definition id, the version, the
  node id and the input name, never the value, its length or its expression text.
- **Continue the pass.** The pass moves on to the next item. A pass whose only problems are refusals completes
  normally, publishes `WorkflowVersionsReconciled` for the items it did reconcile, and does not block host
  readiness. Export pushes whatever it committed.

## Composition

`WorkflowStateAdmission` is a required constructor dependency of the six Design API callers (Definitions/Add,
Drafts/Replace, Definitions/Update, Versions/Add, Definitions/Submit, Drafts/Promote), `WorkflowsVersionReconciler`
and `GitWorkflowExporter`; it wraps `ICredentialLiteralValidator`. `WorkflowsDesignApi`, `JsonWorkflowReconciliation` and
`WorkflowsDesignGitReconciliation` (the two concrete features deriving from `WorkflowsDesignReconciliationFeature`)
declare `DependsOn` on `WorkflowDesignValidations`, so a host that composes them without the rule fails at
composition instead of silently skipping it. The persistence features declare nothing new.

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
2. **Caller coverage, the one assertion.** A source scan of `src/` finds every non-persistence type whose
   constructor takes an admitted command, and asserts that the same constructor takes `WorkflowStateAdmission`.
   The scan must find at least the seven known callers (the six Design API callers and `WorkflowsVersionReconciler`),
   so it cannot pass by scanning nothing.
3. **Git export.** The exporter calls no design command, so it is listed by name: every `IGitWorkflowExporter`
   implementation takes `WorkflowStateAdmission`.

Mutations that must turn it red: remove `WorkflowStateAdmission` from one Design API caller (the promote endpoint
among them); add a new admitted-command caller without it; add a new `*Command` contract without classifying it.
Whether a caller actually calls the helper before the command is proved per entry point by T051 to T054, whose
bite-proofs remove the call.

## Known gaps (not covered by this rule)

- **Activities not in the catalog** (spec FR-008 and its edge case). The validator, like
  `RequiredInputOutputValidator`, skips nodes whose activity version the catalog cannot resolve, so it cannot tell
  whether their inputs are credentials. Draft save, add-version, submit, file reconciliation and git export cannot
  judge such nodes and accept them. Publish resolves every node and refuses there; promote refuses once the activity
  is installed. Until then such a literal can be stored.
- **Runtime artifact import** carries compiled bindings and is not one of the seven entry points (research R13).
  R8's producer withholding and the `VF-ACT-010` activation refusal keep such a value out of persisted runtime
  state.
