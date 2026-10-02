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
| Input sensitivity declaration | `Elsa.Activities.Runtime.Core` (`ActivityInputAttribute`, `ActivityInputContract`), `Elsa.Activities.Design.Core` (`InputDefinition`), `Elsa.Activities.Design.Api` (`ActivityInputDescriptorView`) | New properties | Attribute: `bool IsSensitive`, `bool IsCredential`. Catalog: `bool? IsSensitive`, `bool? IsCredential`, null unless true, so undeclared activities keep their catalog hash. Pinned contract: explicit `IsCredential`, so the credential rule never infers it from `RequiresEncryption`. Wire view: non-null `IsSensitive`, `IsCredential`. Credential implies sensitive (normalized by the scanner). A credential input cannot declare a default, must be of CLR type `string`, and cannot sit where a `Secret` binding is refused (a checkpoint participant, or an input named by `[RefusesSecretBinding]`); the Activities Design API refuses the credential flag on the versions it accepts. |
| Secret binding refusal declaration | `Elsa.Activities.Runtime.Core` | New: class-level `RefusesSecretBindingAttribute(InputKey, Reason)`, enum `SecretBindingRefusalReason { PersistedByActivity, FixedAtPublish, EchoedToOutput }` | An activity type names an input that cannot take a `Secret` binding, because the activity copies its value into its own persisted state, returns it in its result or copies it into a fault it reports, or a publish-time reader needs it as a literal. Read at publish by `ExecutableNodeCompiler` (`VF-ACT-012`) and by the scanner (credential conflict); never written to the catalog, so catalog hashes are unchanged. |
| Effective value policy | `Elsa.Activities.Runtime.Core` (`ActivityValuePolicy`), `Elsa.Workflows.Runtime.Core` (`ValuePolicyCombiner`) | Existing, extended | Stricter of declaration and authored choice (FR-007). Credential sets `RequiresEncryption`. An explicit authored `IsSensitive: false` against a sensitive or credential declaration is refused (`VF-ACT-005`). |
| Credential-literal rule | `Elsa.Workflows.Design.Core` (predicate, rule id), `Elsa.Workflows.Design.Validations.Core` (`ICredentialLiteralValidator`, `CredentialLiteralRefusedException`), `Elsa.Workflows.Design.Validations` (implementation, also registered as an `IDraftValidator`) | New; enforced only in the application layer (Design API admission including promote, reconciler, compiler, git exporter), never in persistence. Every caller of a state-writing design command and the exporter go through one `public sealed` helper, `WorkflowStateAdmission` (`Elsa.Workflows.Design.Validations.Core`) | Rule id `Inputs/CredentialLiteral`. Finding = existing `ValidationError(Path: "{nodeId}/inputs/{referenceKey}", Type: "Inputs/CredentialLiteral", Message)`. The message names the activity id and input name and never echoes the bound value. |
| Runtime secret resolution | `Elsa.Workflows.Runtime.Core` | New: `IRuntimeSecretResolver`, `RuntimeSecretResolutionRequest(TenantId, Reference)`, `RuntimeSecretResolution(Succeeded, Value?, FailureCode?, IsRetryable)` | Replacement contract (§2.6.2). Never throws for domain failures; throws only on cancellation. See [the resolution contract](contracts/runtime-secret-resolution.md). |
| Secret resolution fault | `Elsa.Workflows.Runtime.Core` | New: `RuntimeSecretResolutionException(ReferenceName, FailureCode, IsRetryable)` | Message `Secret '<name>' could not be resolved (<code>).` No value, no store detail. `FailureCode` is one of: the bridge's codes (`NotFound`, `Inactive`, `Expired`, `Revoked`, `Deleted`, `TypeMismatch`, `ScopeMismatch`, `StoreUnavailable`, `Unauthorized`, `CorruptState`), `ConversionFailed` from a conversion failure at activation (a backstop that only an artifact skipping publish can reach; `TypeMismatch` means only that the stored secret's type differs from the reference's), `TenantMismatch` from the tenant check (FR-005), or `ResolverFailed` when the resolver throws instead of returning a result (permanent; what it threw is dropped, never chained). Only `StoreUnavailable` is retryable. A missing bridge is not a failure code: it is an activation failure that parks the activity (FR-003). |
| Fault classification | `Elsa.Workflows.Runtime.Core` | New: `IRuntimeFaultClassification(IsRetryable, FailureCode)` | Implemented by `RuntimeSecretResolutionException` and copied by `SecretMaskedException`. The fault recorder reads retryability through it, so masking never changes a fault's classification. |
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
   | CLR activation (invoke, resume, structural, re-materialized child completion, operator reschedule):
   |   tenant check, then IRuntimeSecretResolver(tenant = partition)
   |-- success: convert with plan -> hydrate CLR property (memory only) -> IRuntimeSecretMask.Register
   |-- domain failure: RuntimeSecretResolutionException -> activity Faulted (IsRetryable only for StoreUnavailable)
   |-- instance tenant set and != partition: TenantMismatch fault, resolver never called
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
| A `Secret` binding on an intrinsic node, a graph activity, a checkpoint participant, an input named by `[RefusesSecretBinding]`, the `[ActivityValueOutcomes]` input or a variable default is refused (`VF-ACT-012`) | `ExecutableNodeCompiler`; each publish-time literal reader also throws the fixed `VF-ACT-012` message for `SecretRead` | FR-001, R12, R3a |
| A credential input on a checkpoint participant, on an input named by `[RefusesSecretBinding]`, or of a CLR type other than `string` is refused; the Activities Design API refuses `isCredential` | `ClrAssemblyScanner`; `AddDefinitionCommandHandler`, `AddVersionCommandHandler` | FR-006, R5 |
| A literal or expression binding on an input whose effective policy requires encryption is refused (`VF-ACT-011`) | `RuntimeInputBindingCompiler` | FR-010, R8 |
| Resolution refuses when the instance tenant is set and differs from the partition (`TenantMismatch`) | `ActivityActivator` | FR-005, R2 |
| A `Secret` binding whose target type has no conversion plan from a `String` text value is refused (`VF-COER-001`): only a single `String` or `Elsa.Any`, `Any` or `JsonNode` input accepts one, for every secret type and in every host | `RuntimeInputBindingCompiler` through `ValueConversionPlanResolver` | FR-004, R11 |
| Promote writes only the draft content its endpoint admitted; a draft changed in between is refused with a conflict | `EfPromoteDraftToVersionCommand` (storage-integrity compare of the `StateSource` hash, no rule) | FR-008, R7 |
| No commit carries a present inline or external envelope whose policy requires encryption | `RuntimeCheckpointCommitValidator` | FR-010 backstop |
| A withheld value that is not a secret reference is never hydrated | `ActivityActivator` (`VF-ACT-010`), backstop behind `VF-ACT-011` | FR-010 |
