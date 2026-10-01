# Acceptance proof matrix

Required for implementation. No row is claimed executed by this specification task. "Bite-proof" names the single
revert or mutation that must turn the row's key test red; a row whose test stays green under its bite-proof guards
nothing and must be reported as such in the PR. Slice numbers refer to [tasks.md](../tasks.md#delivery-slices).

| ID | Requirements | Proof | Expected observation | Bite-proof | Tasks | Slice |
|---|---|---|---|---|---|---|
| A01 | FR-001, FR-004, SC-001 | Publish a definition binding a test activity's credential input to `Secret`; run with a fake `IRuntimeSecretResolver` (slice 3) and then the real bridge (slice 4) | Activity receives the value converted to the declared type; definition and version hold only the reference | Make the materializer emit the binding as an empty literal instead of `SecretRead`: activity-received assertion red | T010, T018, T026 | 2, 3, 4 |
| A02 | FR-002, SC-002 | Rotate the secret between two runs; and rotate while a run is suspended, then resume | Second run and the resumed activation see the new value; no republish | Cache the first resolved value in the activator per process: rotation assertion red | T019, T026 | 3, 4 |
| A03 | FR-002, FR-011 | After the suspend/resume run, read every persisted activity state row | Rows hold the withheld envelope, never the value | Resolve in the materializer and put the value inline: persisted-row assertion red | T011, T019 | 2, 3 |
| A04 | FR-005, US1.4 | Two tenants, same secret name, different values; run each tenant's instance; then an instance whose `TenantId` differs from its partition | Each sees its own value on every resolving path of research R3a; a global or across-scope context refuses before resolution; a mismatched instance faults with `TenantMismatch` and the resolver is never called | Replace the partition read with `"default"`: two-tenant assertion red. Remove the mismatch check: mismatch case red | T018, T026, T093 | 3, 4 |
| A05 | FR-003, SC-006 | One run per failure code (NotFound, Inactive, Expired, Revoked, Deleted, TypeMismatch, ScopeMismatch, StoreUnavailable, Unauthorized, CorruptState) plus a conversion failure and a tenant mismatch | Fault names the reference and code, contains no value and no `ResolvedSecret.Error` text; `IsRetryable` true only for StoreUnavailable | Map StoreUnavailable to non-retryable: classification row red. Pass `Error` into the message: no-store-detail row red | T031, T032, T036, T037 | 3, 4 |
| A06 | FR-003, §E2.6.1 | Run a secret-bound workflow in a host without `SecretsWorkflows` | Activity waits with an activation-failure incident naming the missing composition; it is not faulted | Throw a plain fault instead: waiting-status assertion red | T032, T037 | 3, 4 |
| A07 | FR-006 | Reconcile a test activity declaring sensitive and credential inputs; read the catalog, the pinned contract and the authoring view; reconcile a credential input on a checkpoint participant and on an input its type names in `[RefusesSecretBinding]`; add an activity definition version with `isCredential: true` through the Activities Design API | `InputDefinition`, `ActivityInputContract` and `ActivityInputDescriptorView` carry the flags; an undeclared activity's catalog hash and contract fingerprint are unchanged, also when the type carries `[RefusesSecretBinding]`; the two unbindable credential declarations are refused naming type and input; the API refuses the credential flag | Write `false` instead of null: hash-stability assertion red. Remove the scanner's unbindable-credential check: both refusal rows red. Remove the API check: its row red | T040, T041, T042, T102 | 5 |
| A08 | FR-007 | Compile each row of the effective-policy table, including explicit `false` against a declaration | Stricter policy applies; downgrade refused with `VF-ACT-005` | Remove the downgrade check: refusal assertion red | T042 | 5 |
| A09 | FR-008, FR-009, SC-003 | Push one definition with a literal on a credential input through all seven entry points; repeat with an expression and with a variable binding; repeat with a secret reference and with an empty string; promote a draft stored directly in a host without `IInlineEventPublisher` | Each of 7 refuses with `Inputs/CredentialLiteral`, node id and input name; nothing stored at draft save; promote refuses through admission (400) whatever the promotion command's composition; reconciliation and export refuse that item only, log a value-free warning and complete; secret and empty bindings accepted everywhere; a sensitive non-credential literal accepted; no persistence file changed | Remove the admission from one entry point at a time: that entry point's row red (seven separate bite-proofs; promote's red in both hosts); T055's three mutations red | T050 to T055 | 6 |
| A10 | FR-010 | Commit a state change carrying a present envelope that requires encryption | Commit refused with `VF-ACT-005` | Remove the backstop rule: refusal assertion red | T065 | 7 |
| A11 | FR-010 | Hydrate a `PolicyRequiresEncryption` withheld envelope (reachable only by skipping publish) | `VF-ACT-010`, no null hydration; a checkpoint participant handed a withheld envelope gets the same fixed code | Hydrate null instead: assertion red | T008 | 2, 3 |
| A12 | FR-011 | Read evidence, run inspector (instance, activity execution, descendants, value-evidence payload, executable bindings) and diagnostic snapshots for a secret-bound run | Reference plus withheld marker; no value; no surface throws on `Withheld` | Remove the withheld branch from one surface: that surface's assertion red | T012, T067 | 2, 7 |
| A13 | FR-012, FR-003 | Activity throws an exception whose message contains the value; separately returns an `ActivityFault` whose message contains it; separately a first secret resolves and a second fails with `StoreUnavailable` | Persisted fault, incident, captured logs and exported telemetry show `[secret:<name>]`, never the value; the masked `StoreUnavailable` fault stays retryable with its code and type name | Skip mask registration in the activator: all four assertions red. Skip the `ActivityFault` masking only: the returned-fault assertion red. Stop copying the classification: the retryable assertion red | T073, T094 | 8 |
| A14 | FR-013, SC-004 | Canary: 4 run shapes (success, faulting exception, suspend/resume, rotation) then scan 8 surfaces, each after its precondition holds | Value appears 0 times across all surfaces and shapes; every surface's precondition holds | Empty one surface's source: its precondition red, not its zero count. See A15 for the protections | T079, T080, T081 | 9 |
| A15 | FR-011, FR-012, FR-014, SC-005 | Disable each protection of the [per-protection table](#per-protection-bite-proof-a15) alone, in its named scenario | Each disablement turns the canary red on the named surface or assertion; the PR records one row per protection id | The table itself; a protection whose disablement stays green is reported as unguarded and blocks the slice | T098, T081, T083 | 9 |
| A16 | FR-015 to FR-017 | Studio vitest suites, including the SDK typing-copy test | The canonical SDK and each of the three hand-maintained `studio-sdk.d.ts` copies declare `isSensitive` and `isCredential` on `StudioActivityInputDescriptor`; credential input resolves to the secret picker with literal entry unavailable, and to the existing unavailable state when the Secrets module is absent; `password` hint resolves to the masked editor ahead of single-line; no prefill | Remove the field from one copy: the typing-copy test red. Remove the credential filter, then the editor registration: each test red | T084, T085, T086 | 10 |
| A17 | FR-018 | Search spec 079 for `ISecretResolver` and `src/Elsa/Secrets` | No matches | Reintroduce one occurrence: the search finds it | T002, T003 | 1 |
| A18 | FR-010 | Publish a literal, object, variable, JavaScript expression and a contract default on a non-credential input whose effective policy requires encryption | Each refused with `VF-ACT-011`, value-free; `Secret` and unbound accepted; a credential input reports `Inputs/CredentialLiteral` instead | Remove the `VF-ACT-011` check: literal and expression rows red | T095, T054 | 5, 6 |
| A19 | FR-001, FR-002, FR-011 | One test per input path of research R3a (IP1 to IP21), including `Secret` bindings on intrinsic, graph and checkpoint-participant nodes, on inputs named by `[RefusesSecretBinding]` (activity-persisted and publish-time), on the `[ActivityValueOutcomes]` input and on a variable default | Each CLR path resolves at the point of use and persists nothing; every refused case fails at publish with `VF-ACT-012` naming node and input; each publish-time literal reader handed a `SecretRead` binding throws the fixed `VF-ACT-012` message; the boundary retry calls neither activator nor resolver | Remove the graph-consumer refusal: graph row red. Remove the attribute check from the compiler: the named-input rows red. Drop `ForEach`'s attribute: its row red. Drop one reader's explicit `SecretRead` case: its backstop row red. Skip resolution on the re-materialized activation: IP6 row red | T010, T091, T008, T099 | 2, 3 |
| A20 | FR-004, edge case "type cannot represent the secret" | Publish `rsa-key` and `x509-certificate` references on integer, boolean, date/time, enum and integer-collection inputs, and on string, string-collection, `Object`, `Elsa.Any` and `JsonElement` inputs; a `text` reference on an integer input; an untyped reference; each with and without the bridge's type domains | With the bridge: `StructuredText` on the numeric, boolean, date/time, enum and integer-collection inputs refused with `VF-ACT-013`; on string, string-collection, `Object`, `Elsa.Any` and `JsonElement` accepted. Without the bridge: nothing refused by `VF-ACT-013`. An impossible conversion faults at run time with `TypeMismatch` | Remove the domain check: `rsa-key` to integer row red. Refuse every non-string type: the `Object`, `Elsa.Any` and `JsonElement` rows red | T010, T025 | 2, 4 |
| A21 | FR-009, edge case "non-credential sensitive input bound to an expression" | Publish and run a JavaScript expression bound to a non-credential input that the activity declares sensitive and whose effective policy does not require encryption | Compiles with effective policy `{IsSensitive: true, RequiresEncryption: false}`; neither `Inputs/CredentialLiteral` nor `VF-ACT-011` refuses it; the materialized envelope is present (not withheld) and carries that policy | Apply the credential rule to sensitive inputs, or `VF-ACT-011` to every sensitive input: the compile row red | T104 | 5 |

## Per-protection bite-proof (A15)

Every protection that FR-011 and FR-012 rely on is listed. The list is closed: a protection added later must be added
here before its slice merges, and the canary PR's table must list exactly these ids. "Injection" names the test-only
seam (T098, research R10) that removes the upstream protection for that scenario only, so the protection under test
is the only thing between a planted value and the surface. A scenario with an injection asserts its own positive
control (the planted value is present where the injection put it) and leaves that surface out of its scan.

Scenarios: **S1** success and rotation; **S2** throw with the value on invoke, and S2b return an `ActivityFault`
with the value; **S3** suspend and resume, and S3b throw with the value on resume; **S4** structural activity throws
with the value in its child-completion callback; **S5** imported runtime artifact binding literal canary value C2 to
an input whose pinned policy requires encryption (no publish, research R13); **S6** like S5, plus the materializer
decorator re-planting C2 as a present inline envelope with `RequiresEncryption`; **S7** publish a `Secret` binding on
a Set Variable intrinsic and on a graph activity input; **S10** like S1, plus the materializer decorator in its
lowering mode: before delegating to the real materializer it replaces the canary `SecretRead` binding's policy with
`{IsSensitive: false, RequiresEncryption: false}` (the shape a hand-built imported artifact can already carry,
research R13), so producer withholding, the commit backstop and every sensitive-value rule stand aside. C1 is the
canary secret's own value. S10's positive control: with every protection enabled, the run completes, the activity
received C1, and the persisted snapshot holds a `SecretReference` withheld envelope carrying the lowered policy;
S10 plants nothing, so it excludes no surface; **S8** the materializer decorator re-plants canary value C3 as
a present inline envelope with `IsSensitive: true` and `RequiresEncryption: false` (the backstop does not apply by
its own rule); S8b is S8 with the capture-everything `IRuntimePayloadCapturePolicy` double; **S9** a decorator over the invoke scheduler work handler throws an exception whose
message is canary value C4 after the handler returns, as a handler that let an unmasked exception escape would (the
drainer records it as a handler fault; poison records and logs are planted surfaces). Runtime spans never carry
activity fault text today, because handlers record activity faults themselves; S9 is the only way to make the span
protection the last one standing.

| ID | Protection (code) | Scenario | Injection | Disabled by | Expected red |
|---|---|---|---|---|---|
| P1 | Withheld materialization: `SecretRead` becomes `Withheld` (`RuntimeActivityInputMaterializer`) | S10 | materializer decorator, lowering mode | resolve in the materializer and emit a present inline envelope | runtime database scan finds C1 (the Scheduled to Running snapshot) |
| P2 | No write-back: the hydrated values never reach a committed snapshot (`ActivityActivator`, invoke handler) | S10 | materializer decorator, lowering mode | commit the activator's resolved snapshot as the invoke handler's value-flow snapshot | runtime database scan finds C1 (the completion commit) |
| P3 | Producer withholding (`RuntimeExternalEnvelopeStorage.RewriteAsync`) | S5 | imported artifact | return the envelope unchanged | S5 assertions: `PolicyRequiresEncryption` marker in persisted activity state and fault code `VF-ACT-010` (P4 refuses instead with `VF-ACT-005`) |
| P4 | Commit backstop (`RuntimeCheckpointCommitValidator`) | S6 | imported artifact and materializer decorator | remove the encryption rule | runtime database scan finds C2 |
| P5 | Publish refusal of `Secret` on intrinsic, graph and checkpoint-participant nodes (`ExecutableNodeCompiler`, `VF-ACT-012`) | S7 | none | remove the refusal | S7 publish-refused assertion |
| P6 | Withheld rendering on the invoke inspection path: inspection renders the reference and never resolves it (`ActivityExecutionInspection.BuildInputValueSnapshots`) | S10 | materializer decorator, lowering mode | resolve the withheld reference to render the input | diagnostic snapshot surface finds C1 in the secret-bound input's inspection projection |
| P7 | Payload capture declines sensitive payloads (`DefaultRuntimePayloadCapturePolicy`) | S8 | materializer decorator | capture sensitive payloads | diagnostic snapshot surface finds C3 |
| P8 | Evidence redacts sensitive values (`ExecutionEvidenceCheckpointEnricher`) | S8 | materializer decorator | skip redaction for sensitive envelopes | execution evidence surface finds C3 |
| P9 | Run-inspector and executable-inspector views hide sensitive values (`Elsa.Workflows.Runtime.Api` views, `WorkflowExecutableInspector`) | S8b | materializer decorator and capture-everything policy | return the value for sensitive inputs | inspector responses surface finds C3 |
| M1 | Mask registration of resolved values (`ActivityActivator`) | S2 | none | skip registration | runtime database (UTF-16LE Base64 of the fault message) and captured logs |
| M2 | Exception masking at the invoke boundary (`WorkflowInvokeActivitySchedulerWorkHandler`) | S2 | none | pass the original exception through | runtime database and captured logs |
| M3 | Exception masking at the resume boundary (`WorkflowResumeBookmarkSchedulerWorkHandler`) | S3b | none | pass the original exception through | runtime database and captured logs |
| M4 | Exception masking at the structural boundary (`StructuralParentEvaluationSupport`) | S4 | none | pass the original exception through | runtime database and captured logs |
| M5 | `ActivityFault.Message` masking before `ActivityFaultProjection.ToNormalized` | S2b | none | skip it | runtime database (persisted fault) |
| M6 | Runtime spans carry the exception type only (`WorkflowSchedulerDrainer` sets the error status to the captured type name) | S9 | work-handler decorator | add the exception message to the span status | runtime telemetry surface finds C4 |

SC-005 is proved when the canary PR's table has one red row per id above (15 rows) and none green. Each injection
seam is test-only DI composition in the canary host; production code gains no switch. Every expected-red cell names
a surface the scan reads, or (P3, P5) an assertion about the protection's own refusal; no row counts a run failure
caused by a different protection.

**Surfaces guarded outside the canary.** The canary binds a `Secret` reference, so no protection it exercises stands
between a value and the stored-definition or git-export surface: the only thing that keeps a credential out of those
surfaces is the credential-literal rule (FR-008). Those surfaces are guarded by that rule's own bite-proofs in A09:
the draft save, promote, add-version and submit rows of T051 and T052, the publish row of T054, the reconciliation and
git-export rows of T053, and T055's coverage-guard mutations. Story 4's "any single protection" is therefore traced
by A15 for the runtime surfaces and by A09 for the definition surfaces.

## Canary surfaces (FR-013) and how each is read

| Surface | Read as | Precondition before the scan counts |
|---|---|---|
| Stored definitions and versions | design SQLite files (all files in the directory, including `-wal`) | the canary version row exists and its state holds the reference name |
| Git export output | every file in the export working tree, plus `git log -p` output of the export branch (git stores blobs compressed, so a raw scan of `.git/objects` would see nothing) | the canary version file and its commit exist |
| Persisted instance and activity state | runtime SQLite files; see the encoding note below | the canary instance exists and an activity row carries the withheld marker |
| Execution evidence | evidence store or captured evidence records | at least one record for the canary instance, with the withheld disposition |
| Inspector responses | serialized responses of `WorkflowsRuntimeApi`: instance, activity execution, descendants, value-evidence payload, incidents, and the executable inspector | each response is 200 and names the canary instance or artifact |
| Diagnostic snapshots | inspection projections and diagnostic snapshot payloads, read through the store at diagnostics levels `DiagnosticSnapshot` and `Payload` | the companion input's projection holds a non-empty diagnostic snapshot for the canary run |
| Runtime telemetry export | a `System.Diagnostics.ActivityListener` on the runtime activity sources, recording span names, tags, events and status (the pattern `tests/essentials/Workflows/Runtime/Tests/RuntimeEngineTracingTests.cs` uses) | at least one span belongs to the canary instance (and, in S9, has an error status) |
| Runtime-captured logs | capturing logger provider registered on the host for all categories | at least one line names the canary instance id |

**Encoding note**: runtime `ContentJson` writes CLR strings as Base64 of UTF-16LE (`RuntimeArtifactJson`,
`LosslessUtf16StringConverter`). The scanner searches, per surface, the raw UTF-8 value, its JSON-escaped form, and
all three Base64 alignments of both its UTF-8 and UTF-16LE encodings. Without this, a leaked fault message in the
runtime database is invisible to the scan and the canary passes while leaking. The A15 bite-proof is the scanner's
control: M1 to M4 disabled must be found in the runtime database through the UTF-16LE Base64 search, not only in logs.

**Mode note**: input snapshots are handled in both discrete and fused start/invoke stages. If the canary host can
select both, the canary runs in both; if it cannot, the PR says which mode ran.

## Traceability

| Requirement | Rows |
|---|---|
| FR-001 | A01, A19 |
| FR-002 | A02, A03, A19 |
| FR-003 | A05, A06, A13 |
| FR-004 | A01, A20 |
| FR-005 | A04 |
| FR-006 | A07 |
| FR-007 | A08 |
| FR-008 | A09 |
| FR-009 | A09, A21 |
| FR-010 | A10, A11, A18 |
| FR-011 | A03, A12, A15, A19 |
| FR-012 | A13, A15 |
| FR-013 | A14 |
| FR-014 | A15 |
| FR-015 to FR-017 | A16 |
| FR-018 | A17 |
| SC-001 | A01 |
| SC-002 | A02 |
| SC-003 | A09 |
| SC-004 | A14 |
| SC-005 | A15 |
| SC-006 | A05 |
