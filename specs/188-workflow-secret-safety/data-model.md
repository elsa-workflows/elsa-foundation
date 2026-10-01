# Data model: Workflow secret safety

Read with the [spec](spec.md), [research](research.md) and the [contracts](contracts/). Every type below marked
"new" is a proposed implementation shape, not an existing API. Names were checked against constitution §E6
(R1 to R8); the reasoning is in [plan.md](plan.md#constitution-check).

## Entities

| Entity | Owner (project) | Status | Fields and rules |
|---|---|---|---|
| Secret reference (authored) | `Elsa.Workflows.Design.Core` (`ArgumentValue`) | Existing | `ArgumentValue(Value, ExpressionType: "Secret")` with `Value = {name, typeName?, scope?}` as emitted by the Studio secret picker. The only secret-related content a definition may hold. Unchanged. |
| Secret reference (runtime) | `Elsa.Workflows.Runtime.Core` | New: `RuntimeSecretReference(Name, TypeName?, Scope?)` | Mirrors `Elsa.Secrets.Core.Models.SecretReference` without referencing it. `Name` non-blank. No tenant field: a binding can never select a tenant (FR-005). |
| Secret-read binding | `Elsa.Workflows.Runtime.Core` (`RuntimeInputBinding`) | New source `RuntimeInputBindingSource.SecretRead`, new payload property `Secret` | Exactly one role-owned payload (existing `ValidateCanonical` rule extended). Carries a `ConversionPlan` from `string` to the input's target type, compiled at publish. `EffectivePolicy` always includes `IsSensitive` and `RequiresEncryption`. |
| Withheld value | `Elsa.Workflows.Runtime.Core` (`ValueEnvelope`) | New presence `ValuePresence.Withheld`, new record `WithheldValue(Kind, Secret?, ConversionPlan?)`, new enum `WithheldValueKind { SecretReference, PolicyRequiresEncryption }` | A withheld envelope carries no inline, external or transient payload. `Kind = SecretReference` requires `Secret`; `PolicyRequiresEncryption` forbids it. This is what persisted and inspected state records in place of a secret-derived or encryption-requiring value (FR-010, FR-011). |
| Input sensitivity declaration | `Elsa.Activities.Runtime.Core` (`ActivityInputAttribute`), `Elsa.Activities.Design.Core` (`InputDefinition`), `Elsa.Activities.Design.Api` (`ActivityInputDescriptorView`) | New properties | Attribute: `bool IsSensitive`, `bool IsCredential`. Catalog: `bool? IsSensitive`, `bool? IsCredential`, null unless true, so undeclared activities keep their catalog hash. Wire view: non-null `IsSensitive`, `IsCredential`. Credential implies sensitive (normalized by the scanner). A credential input cannot declare a default. |
| Effective value policy | `Elsa.Activities.Runtime.Core` (`ActivityValuePolicy`), `Elsa.Workflows.Runtime.Core` (`ValuePolicyCombiner`) | Existing, extended | Stricter of declaration and authored choice (FR-007). Credential sets `RequiresEncryption`. An explicit authored `IsSensitive: false` against a sensitive or credential declaration is refused (`VF-ACT-005`). |
| Credential-literal rule | `Elsa.Workflows.Design.Core` (predicate, rule id), `Elsa.Workflows.Design.Validations.Core` (`ICredentialLiteralValidator`, `CredentialLiteralRefusedException`), `Elsa.Workflows.Design.Validations` (implementation) | New | Rule id `Inputs/CredentialLiteral`. Finding = existing `ValidationError(Path: "{nodeId}/inputs/{referenceKey}", Type: "Inputs/CredentialLiteral", Message)`. The message names the activity id and input name and never echoes the bound value. |
| Runtime secret resolution | `Elsa.Workflows.Runtime.Core` | New: `IRuntimeSecretResolver`, `RuntimeSecretResolutionRequest(TenantId, Reference)`, `RuntimeSecretResolution(Succeeded, Value?, FailureCode?, IsRetryable)` | Replacement contract (§2.6.2). Never throws for domain failures; throws only on cancellation. See [the resolution contract](contracts/runtime-secret-resolution.md). |
| Secret resolution fault | `Elsa.Workflows.Runtime.Core` | New: `RuntimeSecretResolutionException(ReferenceName, FailureCode, IsRetryable)` | Message `Secret '<name>' could not be resolved (<code>).` No value, no store detail. |
| Secret value mask | `Elsa.Workflows.Runtime.Core` (contract), `Elsa.Workflows.Runtime` (scoped implementation) | New: `IRuntimeSecretMask`, `SecretMaskedException` | In memory only, per DI scope. `Register(activityExecutionId, referenceName, value)` and `Mask(activityExecutionId, text)`. Never persisted, logged or serialized. |
| Secrets bridge | `Elsa.Secrets.Workflows` | New project and feature `SecretsWorkflows` | `SecretValueRuntimeResolver : IRuntimeSecretResolver` over `ISecretValueResolver`. Maps failure codes; discards `ResolvedSecret.Error`. |

## Persisted shapes (illustrative JSON, camelCase as `RuntimeArtifactJson` writes it)

The member names of `ValueTypeDescriptor`, `ValueProtectionPolicy` and `ValueConversionPlan` are abbreviated here;
the implementation serializes the existing types unchanged. Only `source: "SecretRead"`, `secret`,
`presence: "Withheld"` and `withheld` are new.

Secret-read binding inside a published `WorkflowExecutable`:

```json
{
  "inputName": "apiKey",
  "targetType": { "alias": "string", "collectionKind": "Single" },
  "effectivePolicy": { "lifecycle": "Instance", "storage": "Inline", "isSensitive": true, "requiresEncryption": true },
  "source": "SecretRead",
  "secret": { "name": "payments.api-key", "typeName": "text" },
  "conversionPlan": { "operation": "Identity" }
}
```

Withheld envelope inside a persisted activity input snapshot:

```json
{
  "type": { "alias": "string", "collectionKind": "Single" },
  "presence": "Withheld",
  "withheld": {
    "kind": "SecretReference",
    "secret": { "name": "payments.api-key", "typeName": "text" },
    "conversionPlan": { "operation": "Identity" }
  },
  "policy": { "lifecycle": "Instance", "storage": "Inline", "isSensitive": true, "requiresEncryption": true }
}
```

Note: `RuntimeArtifactJson` encodes CLR `string` properties as Base64 of UTF-16LE (see [research R10](research.md#r10-the-canary-harness-fr-013-fr-014)).
The secret name above is therefore not readable as plain text in the database row. That is acceptable: names are
not secret material. It is also why the canary scanner searches encoded forms.

## State transitions of a secret-bound input

```text
authored    ArgumentValue { expressionType: "Secret", value: {name, typeName?, scope?} }
   | credential-literal rule (7 entry points): accepted
published   RuntimeInputBinding { source: SecretRead, secret, conversionPlan, policy >= {sensitive, encrypt} }
   | Scheduled -> Running: materializer, no resolution
persisted   ValueEnvelope { presence: Withheld, withheld: { kind: SecretReference, secret } }
   | activation (invoke, resume, retry, structural): IRuntimeSecretResolver(tenant = execution partition)
   |-- success: convert with plan -> hydrate CLR property (memory only) -> IRuntimeSecretMask.Register
   |-- domain failure: RuntimeSecretResolutionException -> activity Faulted (IsRetryable only for StoreUnavailable)
   |-- no resolver composed: activation failure -> activity Waiting with incident (§E2.6.1)
completed   persisted state still holds only the withheld envelope; mask discarded with the DI scope
```

There is no transition back from "hydrated" to "persisted". Nothing writes a hydrated snapshot back to state.

## Validation rules (from requirements)

| Rule | Where | Requirement |
|---|---|---|
| A credential input accepts only `Secret` or no binding | predicate in `Elsa.Workflows.Design.Core`; enforced at the seven entry points | FR-008, FR-009 |
| Empty string, null and JSON null or undefined on a credential input count as unbound | shared `IsBound` predicate (extracted from `RequiredInputOutputValidator`) | Edge case "literal empty string or null" |
| Explicit `IsSensitive: false` against a sensitive or credential declaration is refused | `ValuePolicyCombiner` helper | FR-007 |
| A credential input cannot declare a default | `ClrAssemblyScanner` | R5 |
| A `Secret` binding on an intrinsic node is refused | `ExecutableNodeCompiler` intrinsic path | R12 |
| A `Secret` binding whose target type has no conversion plan from `string` is refused | `RuntimeInputBindingCompiler` | FR-004, R11 |
| No commit carries a present inline or external envelope whose policy requires encryption | `RuntimeCheckpointCommitValidator` | FR-010 backstop |
| A withheld value that is not a secret reference is never hydrated | `ActivityActivator` (`VF-ACT-010`) | FR-010 |
