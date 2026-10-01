# Contract: the credential-literal rule

Proposed shapes for FR-008 and FR-009. Decisions and alternatives are in [research R7](../research.md).

## Identity

- Rule identifier: `Inputs/CredentialLiteral` (the R3 validation-category form `ValidationError.Type` already uses).
- Finding: `ValidationError(Path: "{nodeId}/inputs/{referenceKey}", Type: "Inputs/CredentialLiteral",
  Message: "Input '{inputName}' on activity '{nodeId}' holds a credential and accepts only a secret reference.")`.
- The message and every refusal never echo the bound value, its expression text, or its length.

## Acceptance predicate (`Elsa.Workflows.Design.Core`)

For an activity input whose `InputDefinition.IsCredential == true`, the bound `ArgumentState` is accepted only when:

| Binding | Accepted |
|---|---|
| no `ArgumentState`, or `Value` null | yes (unbound) |
| `Value.Value` null, empty string, JSON null, JSON undefined, JSON empty string | yes (unbound; same definition as `RequiredInputOutputValidator`) |
| `ExpressionType == "Secret"` (ordinal, ignore case, as the compiler already compares types) | yes |
| `Literal`, `Object`, `Default` | no |
| `Variable`, `WorkflowRequest`, `ActivityResult` | no |
| any text expression (`JavaScript`, `Liquid`, ...) | no |

Inputs that are not credentials are unaffected, including sensitive ones (FR-009). The `"Secret"` literal is
duplicated from `Elsa.Secrets.Core.Models.SecretExpressionTypes.Secret` rather than referenced (framework §2.17);
a test in the bridge test project, which references both, pins equality.

## Enforcement and refusal shape at each entry point

| # | Entry point | Where the rule runs | Refusal | HTTP (where an endpoint exists) |
|---|---|---|---|---|
| 1 | Draft save: add definition, create draft, clone draft, update draft (Definitions/Add, Drafts/Replace, Definitions/Update) | guarded state writer on `EfDesignCommand`, before the atomic stage | `CredentialLiteralRefusedException`; nothing stored | 400, errors keyed by path |
| 2 | Promote | same guard on the draft's state inside `EfPromoteDraftToVersionCommand` | same | 400 |
| 3 | Publish (including publish-on-reconcile and draft test runs) | `RuntimeInputBindingCompiler.CompileAll`, per input | same exception, surfaced through publication's existing compile-error translation | 400 |
| 4 | Add version (Versions/Add) | guarded writer in `EfAddWorkflowDefinitionVersionCommand` | same | 400 |
| 5 | Submit (Definitions/Submit) | guarded writer in `EfSubmitWorkflowDefinitionCommand` | same | 400 |
| 6 | File-based reconciliation import (and git import, which feeds it) | guarded writer in `EfMaterializeWorkflowDefinitionVersionCommand` | same; the reconciliation pass fails for that version | n/a |
| 7 | Git export | `GitWorkflowExporter`, before writing a version file | same; the version file is not written and the export pass fails | n/a |

Each refusal carries the rule identifier, the activity (node) id and the input name. The 400 problem body follows
the existing design translator shape (`WorkflowDesignExceptionTranslator.Validation`): `errors` keyed by
`{nodeId}/inputs/{referenceKey}`, each message prefixed with `Inputs/CredentialLiteral`.

## Blocking at draft save

This is the one deliberate exception to "draft save records validation errors without blocking" (spec
clarification). The exception is implemented by the guard throwing before the write, not by changing
`DraftValidationGate`; every other validator keeps the non-blocking draft contract.

## Composition

`ICredentialLiteralValidator` is a required constructor dependency of the eight state-writing EF commands and of
`GitWorkflowExporter`. A host that composes design persistence without `WorkflowDesignValidations` therefore fails
at activation instead of silently skipping the rule. `WorkflowsDesignEntityFrameworkCore` and
`WorkflowsDesignGitReconciliation` declare `DependsOn` on `WorkflowDesignValidations`.

## Known gap (not covered by this rule)

Runtime artifact import carries compiled bindings and is not one of the seven entry points (research R13). The
FR-010 commit backstop refuses to persist such a value at run time.
