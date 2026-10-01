# Acceptance proof matrix

Required for implementation. No row is claimed executed by this specification task. "Bite-proof" names the single
revert or mutation that must turn the row's key test red; a row whose test stays green under its bite-proof guards
nothing and must be reported as such in the PR. Slice numbers refer to [tasks.md](../tasks.md#delivery-slices).

| ID | Requirements | Proof | Expected observation | Bite-proof | Tasks | Slice |
|---|---|---|---|---|---|---|
| A01 | FR-001, FR-004 | Publish a definition binding a test activity's credential input to `Secret`; run with a fake `IRuntimeSecretResolver` (slice 3) and then the real bridge (slice 4), also with a name-only reference | Activity receives the value converted to the declared type; a name-only reference resolves within the tenant; definition and version hold only the reference | Make the materializer emit the binding as an empty literal instead of `SecretRead`: activity-received assertion red | T010, T018, T026 | 2, 3, 4 |
| A02 | FR-002, SC-002 | Rotate the secret between two runs; and rotate while a run is suspended, then resume | Second run and the resumed activation see the new value; no republish | Cache the first resolved value in the activator per process: rotation assertion red | T019, T026 | 3, 4 |
| A03 | FR-002, FR-011 | After the suspend/resume run, read every persisted activity state row | Rows hold the withheld envelope, never the value | Resolve in the materializer and put the value inline: persisted-row assertion red | T011, T019 | 2, 3 |
| A04 | FR-005, US1.4 | Two tenants, same secret name, different values; run each tenant's instance; then an instance whose `TenantId` differs from its partition | Each sees its own value on every resolving path of research R3a; a global or across-scope context refuses before resolution; a mismatched instance faults with `TenantMismatch` and the resolver is never called | Replace the partition read with `"default"`: two-tenant assertion red. Remove the mismatch check: mismatch case red | T018, T026, T093 | 3, 4 |
| A05 | FR-003, SC-006 | One run per failure code (NotFound, Inactive, Expired, Revoked, Deleted, TypeMismatch, ScopeMismatch, StoreUnavailable, Unauthorized, CorruptState) plus a conversion failure (code `ConversionFailed`, constructed in T031 with a conversion executor double that throws, because publish compiles no plan from text that can fail) and a tenant mismatch | Fault names the reference and code, and `TypeMismatch` appears only for a stored secret of another type, contains no value and no `ResolvedSecret.Error` text; `IsRetryable` true only for StoreUnavailable | Map StoreUnavailable to non-retryable: classification row red. Pass `Error` into the message: no-store-detail row red | T031, T032, T036, T037 | 3, 4 |
| A06 | FR-003, §E2.6.1 | Run a secret-bound workflow in a host without `SecretsWorkflows` | Activity waits with an activation-failure incident naming the missing composition; it is not faulted | Throw a plain fault instead: waiting-status assertion red | T032, T037 | 3, 4 |
| A07 | FR-006 | Reconcile a test activity declaring sensitive and credential inputs; read the catalog, the pinned contract and the authoring view; reconcile a credential input on a checkpoint participant, on an input its type names in `[RefusesSecretBinding]`, and of CLR type `int`; add an activity definition version with `isCredential: true` through the Activities Design API | `InputDefinition`, `ActivityInputContract` and `ActivityInputDescriptorView` carry the flags; an undeclared activity's catalog hash and contract fingerprint are unchanged, also when the type carries `[RefusesSecretBinding]`; the three unbindable credential declarations are refused naming type and input; the API refuses the credential flag | Write `false` instead of null: hash-stability assertion red. Remove the scanner's unbindable-credential check: both refusal rows red. Remove the API check: its row red | T040, T041, T042, T102 | 5 |
| A08 | FR-007 | Compile each row of the effective-policy table, including explicit `false` against a declaration | Stricter policy applies; downgrade refused with `VF-ACT-005` | Remove the downgrade check: refusal assertion red | T042 | 5 |
| A09 | FR-008, FR-009, SC-003 | Push one definition with a literal on a credential input through all seven entry points; repeat with an expression and with a variable binding; repeat with a secret reference and with an empty string; promote a draft stored directly in a host without `IInlineEventPublisher` | Each of 7 refuses with `Inputs/CredentialLiteral`, node id and input name; nothing stored at draft save; promote refuses through admission (400) whatever the promotion command's composition; reconciliation and export refuse that item only, log a value-free warning and complete; secret and empty bindings accepted everywhere; a sensitive non-credential literal accepted; no rule entered persistence (the only persistence change is promote's content precondition, A24) | Remove the admission from one entry point at a time: that entry point's row red (seven separate bite-proofs; promote's red in both hosts); T055's four mutations red | T050 to T055 | 6 |
| A10 | FR-010 | Commit a state change carrying a present envelope that requires encryption | Commit refused with `VF-ACT-005` | Remove the backstop rule: refusal assertion red | T065 | 7 |
| A11 | FR-010 | Hydrate a `PolicyRequiresEncryption` withheld envelope (reachable only by skipping publish) | `VF-ACT-010`, no null hydration; a checkpoint participant handed a withheld envelope gets the same fixed code | Hydrate null instead: assertion red | T008 | 2, 3 |
| A12 | FR-011 | Read evidence, run inspector (instance, activity execution, descendants, value-evidence payload, executable bindings) and diagnostic snapshots for a secret-bound run | Reference plus withheld marker; no value; no surface throws on `Withheld` | Remove the withheld branch from one surface: that surface's assertion red | T012, T067 | 2, 7 |
| A13 | FR-012, FR-003 | Activity throws an exception whose message contains the value; separately returns an `ActivityFault` whose message contains it; separately a first secret resolves and a second fails with `StoreUnavailable` | Persisted fault, incident, captured logs and exported telemetry show `[secret:<name>]`, never the value; the masked `StoreUnavailable` fault stays retryable with its code and type name | Skip mask registration in the activator: all four assertions red. Skip the `ActivityFault` masking only: the returned-fault assertion red. Stop copying the classification: the retryable assertion red | T073, T094 | 8 |
| A14 | FR-013, SC-004 | Canary: 4 run shapes (success, faulting exception, suspend/resume, rotation) then scan 8 surfaces, each after its precondition holds | Value appears 0 times across all surfaces and shapes; every surface's precondition holds | Empty one surface's source: its precondition red, not its zero count. See A15 for the protections | T079, T080, T081 | 9 |
| A15 | FR-011, FR-012, FR-014, SC-005 | Disable each protection of the [per-protection table](#per-protection-bite-proof-a15) alone, in its named scenario | Each disablement turns the canary red on the named surface or assertion; the PR records one row per protection id | The table itself; a protection whose disablement stays green is reported as unguarded and blocks the slice | T098, T081, T083 | 9 |
| A16 | FR-015 to FR-017 | Studio vitest suites, including the SDK typing-copy test | The canonical SDK and each of the three hand-maintained `studio-sdk.d.ts` copies declare `isSensitive` and `isCredential` on `StudioActivityInputDescriptor`; credential input resolves to the secret picker with literal entry unavailable, and to the existing unavailable state when the Secrets module is absent; `password` hint resolves to the masked editor ahead of single-line; no prefill | Remove the field from one copy: the typing-copy test red. Remove the credential filter, then the editor registration: each test red | T084, T085, T086 | 10 |
| A17 | FR-018 | Search spec 079 for `ISecretResolver` and `src/Elsa/Secrets` | No matches | Reintroduce one occurrence: the search finds it | T002, T003 | 1 |
| A18 | FR-010 | Publish a literal, object, variable, JavaScript expression and a contract default on a non-credential input whose effective policy requires encryption | Each refused with `VF-ACT-011`, value-free; `Secret` and unbound accepted; a credential input reports `Inputs/CredentialLiteral` instead | Remove the `VF-ACT-011` check: literal and expression rows red | T095, T054 | 5, 6 |
| A19 | FR-001, FR-002, FR-011 | One test per input path of research R3a (IP1 to IP22), including `Secret` bindings on intrinsic, graph and checkpoint-participant nodes, on inputs named by `[RefusesSecretBinding]` (activity-persisted, returned in a result or fault, and publish-time), on the `[ActivityValueOutcomes]` input and on a variable default | Each CLR path resolves at the point of use and persists nothing; every refused case fails at publish with `VF-ACT-012` naming node and input; each publish-time literal reader handed a `SecretRead` binding throws the fixed `VF-ACT-012` message; the boundary retry calls neither activator nor resolver | Remove the graph-consumer refusal: graph row red. Remove the attribute check from the compiler: the named-input rows red. Drop `ForEach`'s attribute: its row red. Drop `Inline`'s or `Fault`'s attribute: that IP22 row red (`Inline` then reports `VF-COER-001`). Drop one reader's explicit `SecretRead` case: its backstop row red. Skip resolution on the re-materialized activation: IP6 row red | T010, T091, T008, T099, T110 | 2, 3 |
| A20 | FR-004, edge case "type cannot represent the secret" | Publish a `text`, an `rsa-key` and an untyped reference on `String`, `Elsa.Any` and `JsonNode` inputs, and on `Int32`, `Boolean`, `DateTime`, `TimeSpan`, enum, `Guid`, `Uri`, `Object`, `JsonElement`, `JsonObject`, `String` list and `Int32` list inputs; with and without the Secrets bridge composed | `String` compiles with an `Identity` plan and `Elsa.Any` and `JsonNode` with a `CanonicalAny` plan; every other row is refused at publish with `VF-COER-001` naming node and input; identical for every secret type and with or without the bridge (research R11). At run time, a stored secret of another type than the reference names faults with `TypeMismatch` (A05) | Skip plan resolution for `Secret` bindings: the `Int32`, `Object`, `JsonElement` and `String` list rows red. Refuse every target but `String`: the `Elsa.Any` and `JsonNode` rows red | T010 | 2 |
| A21 | FR-009, edge case "non-credential sensitive input bound to an expression" | Publish and run a JavaScript expression bound to a non-credential input that the activity declares sensitive and whose effective policy does not require encryption | Compiles with effective policy `{IsSensitive: true, RequiresEncryption: false}`; neither `Inputs/CredentialLiteral` nor `VF-ACT-011` refuses it; the materialized envelope is present (not withheld) and carries that policy | Apply the credential rule to sensitive inputs, or `VF-ACT-011` to every sensitive input: the compile row red | T104 | 5 |
| A22 | FR-008, SC-003 (second clause), edge case "activity the catalog does not know" | A definition whose node references an activity version the catalog does not hold, with a literal on an input that activity declares as a credential once installed; save it and add it as a version (Versions/Add, or a publisher-less host, since promote with the publisher composed answers 409 from `UnknownActivityVersionValidator`), publish it; then insert a catalog activity version with the node's `ActivityVersionId`, declaring the input a credential, and publish the same version again | Draft save and add-version store it (the documented residual); the first publish is refused and writes no executable artifact or publication; after installation publish refuses with `Inputs/CredentialLiteral` naming node and input | Make the executable compiler skip an unfound activity version: the first publish assertion red. Remove the compiler predicate (T060): the second publish assertion red | T112, T051 | 6 |
| A23 | FR-019, SC-001, US1 independent test | `SendHttpRequest` with `Authorization` bound to a secret, against a local endpoint that asserts the header and answers without repeating it; with a `RequestHeaders` `authorization` entry; with `Authorization` empty; rotation; logs at `Trace`; then the canary scan | The endpoint receives the secret's value verbatim as the only `Authorization` header value; the input wins over `RequestHeaders`; empty leaves `RequestHeaders` unchanged; rotation takes effect without republish; a literal on `Authorization` is refused; the catalog marks only `Authorization` sensitive and credential; neither value appears in the result, the captured logs, or any surface the scanner reads | Stop applying the input: header rows red. Apply it before `RequestHeaders`: precedence row red. Copy request headers into `ResponseHeaders`: result row and runtime-database scan red. Drop `IsCredential`: scanner row and literal-refusal row red. Remove `RedactLoggedHeaders` if T106 needed it: log row red | T105, T106, T108 | 11 |
| A24 | FR-008 (promote judges what it promotes), research R7 | Admit a draft at Drafts/Promote, then store a credential literal into the same draft before the promotion lock (test-only draft-store decorator); call the EF command directly with a stale, a matching and a null expected-state hash; replay a succeeded promote's operation key after the draft changed; check that every promote `Execute` method requires the hash | Drafts/Promote answers 409 and writes no version row, in the standard and in the publisher-less host; the command throws `WorkflowDraftChangedException` for a stale hash, promotes for a matching one and throws `ArgumentException` for a null one, writing nothing; the replay returns the original version id and writes no second version; no promote overload lacks `expectedStateHash` | Remove the in-lock comparison: a version row holding the literal is written. Hash a second read in the endpoint instead of the admitted draft: the API row red. Treat a null hash as no precondition: the null-hash row red. Add the hash to the idempotency material: the replay row red. Add a hash-less overload: T055 red | T113, T055 | 6 |

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
a Set Variable intrinsic and on a graph activity input; **S10** like S1, but the canary secret is bound to the
canary activity's undeclared input `Plain` (authored without `isSensitive`), and the materializer decorator runs in
its lowering mode: before delegating to the real materializer it replaces that `SecretRead` binding's policy with
`{IsSensitive: false, RequiresEncryption: false}` (the shape a hand-built imported artifact can already carry,
research R13). Both halves are needed: `ActivityExecutionInspection.BuildInputValueSnapshots` computes
`isSensitive = value.Policy.IsSensitive || input.Policy.IsSensitive`, and `input.Policy` is the pinned contract's,
which for a declared credential input stays sensitive whatever the binding says, so P7 would decline the payload
and a disabled P6 would stay green. With `Plain`, producer withholding, the commit backstop and every
sensitive-value rule stand aside. Every canary input name avoids `DefaultDiagnosticSnapshotFactory`'s name fragments
(`password`, `secret`, `token`, `apikey`, `api_key`, `authorization`, `credential`, matched case-insensitively with
`-` removed), so no name-based redaction hides the value either (research R10). C1 is the canary secret's own value.
S10's positive control: with every protection enabled, the run completes, the activity received C1 on `Plain`, the
pinned contract policy of `Plain` reports `IsSensitive: false`, the persisted snapshot holds a `SecretReference`
withheld envelope carrying the lowered policy, and `Plain`'s own inspection projection exists and reports
`isSensitive: false`; S10 plants nothing, so it excludes no surface; **S8** the materializer decorator re-plants canary value C3 on
`Primary` as a present inline envelope with `IsSensitive: true` and `RequiresEncryption: false` (the backstop does not apply by
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
| P6 | Withheld rendering on the invoke inspection path: inspection renders the reference and never resolves it (`ActivityExecutionInspection.BuildInputValueSnapshots`) | S10 | materializer decorator, lowering mode, on `Plain` | resolve the withheld reference to render the input | diagnostic snapshot surface finds C1 in `Plain`'s inspection projection |
| P7 | Payload capture declines sensitive payloads (`DefaultRuntimePayloadCapturePolicy`) | S8 | materializer decorator | capture sensitive payloads | diagnostic snapshot surface finds C3 |
| P8 | Evidence redacts sensitive values (`ExecutionEvidenceCheckpointEnricher`) | S8 | materializer decorator | skip redaction for sensitive envelopes | execution evidence surface finds C3 |
| P9 | Run-inspector and executable-inspector views hide sensitive values (`Elsa.Workflows.Runtime.Api` views, `WorkflowExecutableInspector`) | S8b | materializer decorator and capture-everything policy | return the value for sensitive inputs | inspector responses surface finds C3 |
| M1 | Mask registration of resolved values (`ActivityActivator`) | S2 | none | skip registration | runtime database (UTF-16LE Base64 of the fault message) and captured logs |
| M2 | Exception masking at the invoke boundary (`WorkflowInvokeActivitySchedulerWorkHandler`) | S2 | none | pass the original exception through | runtime database and captured logs |
| M3 | Exception masking at the resume boundary (`WorkflowResumeBookmarkSchedulerWorkHandler`) | S3b | none | pass the original exception through | runtime database and captured logs |
| M4 | Exception masking at the structural boundary (`StructuralParentEvaluationSupport`) | S4 | none | pass the original exception through | runtime database and captured logs |
| M5 | `ActivityFault.Message` masking before `ActivityFaultProjection.ToNormalized` | S2b | none | skip it | runtime database (persisted fault) |
| M6 | Runtime spans carry the exception type only (`WorkflowSchedulerDrainer` sets the error status to the captured type name) | S9 | work-handler decorator | add the exception message to the span status | runtime telemetry surface finds C4 |

SC-005 is proved when the canary PR's table has one red row per id above (15 rows) and none green.

**Isolation re-checked after the S10 change (round 3).** P1, P2 and P6 (S10) now run on `Plain`, whose binding and
pinned contract are both not sensitive and whose name no redactor matches, so with each disabled alone nothing else
stands between C1 and the named surface: P1 and P2 put C1 into a committed snapshot that producer withholding and the
commit backstop ignore because the policy does not require encryption, and P6 hands C1 to a capture policy that
captures because `isSensitive` is false. P7, P8 and P9 (S8, S8b) plant C3 on `Primary` with `IsSensitive: true`;
`Primary` matches no name fragment, so disabling P7 leaves only the sensitivity rule it removes, P8 only the
evidence redaction, and P9 (with capture-everything) only the inspector views. P3 and P4 (S5, S6) use the imported
artifact and C2, P5 (S7) is a publish refusal, M1 to M5 use the credential input `Primary` and C1 in fault text, and
M6 (S9) uses C4 in a handler exception; none of them depends on a name or on the contract policy. Each injection
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

Every requirement, success criterion, acceptance scenario and edge case of the spec maps to at least one row. A
case left without a test says why.

| Requirement | Rows |
|---|---|
| FR-001 | A01, A19 |
| FR-002 | A02, A03, A19 |
| FR-003 | A05, A06, A13 |
| FR-004 | A01, A20 |
| FR-005 | A04 |
| FR-006 | A07 |
| FR-007 | A08 |
| FR-008 | A09, A22, A24 |
| FR-009 | A09, A21 |
| FR-010 | A10, A11, A18 |
| FR-011 | A03, A12, A15, A19 |
| FR-012 | A13, A15 |
| FR-013 | A14 |
| FR-014 | A15 |
| FR-015 to FR-017 | A16 |
| FR-018 | A17 |
| FR-019 | A23 |
| SC-001 | A23, A01 |
| SC-002 | A02 |
| SC-003 | A09, A22 |
| SC-004 | A14 |
| SC-005 | A15 |
| SC-006 | A05 |

| Acceptance scenario | Rows |
|---|---|
| US1 independent test | A23 |
| US1.1 current value, converted to the declared type | A01, A23 |
| US1.2 rotation without republish | A02, A23 |
| US1.3 re-resolved after suspend and resume | A02, A03 |
| US1.4 two tenants, same secret name | A04 |
| US2.1 revoked, expired, deleted or missing: named, safe, not retryable | A05 |
| US2.2 store unavailable: transient | A05, A13 (also when masked) |
| US2.3 reference type differs from the stored secret | A05 |
| US3.1 literal or expression refused at every entry point, not stored | A09, A22, A24 |
| US3.2 secret reference accepted | A09 |
| US3.3 Studio offers only the secret picker | A16 |
| US3.4 stricter policy applies, no downgrade | A08 |
| US4.1 persisted state holds the reference and a withheld marker | A03, A12, A14 |
| US4.2 fault message masked | A13, A15 (M1 to M5) |
| US4.3 evidence, inspector, diagnostic snapshots, telemetry | A12, A13, A14, A15 |
| US5.1 descriptor exposes the flags | A16 |
| US5.2 password hint masked, never prefilled | A16 |

| Edge case | Rows |
|---|---|
| Input type cannot represent the secret | A20 |
| Input that cannot resolve at the point of use (intrinsic, graph, checkpoint participant, persisted, returned or copied into a fault, read at publish) | A19, A07 (cannot be declared a credential) |
| Secret assigned to a variable or reused through one | A19 (IP11 Set Variable, IP21 variable default, IP22 results) |
| Name-only reference | A01 |
| Deleted between publish and run | A05 (`Deleted`, `NotFound`; T026) |
| Suspended for days, then resumed | A03, A02 |
| Expression on a credential input; expression on a non-credential sensitive input; literal or expression on an encryption-required input | A09; A21; A18 |
| Definitions created before the rule (pre-rule definitions) | A09: T051 stores the draft directly, as a pre-rule draft would be, and promote and publish refuse it; draft save refuses on the next save (T052) |
| Literal empty string or null on a credential input | A09 (empty string accepted everywhere; T049 covers null, JSON null and undefined) |
| Activity the catalog does not know | A22 |
| Reconciliation or git export meets a refused version | A09 (T053) |
| Same secret consumed by many activities in one run | Not tested on purpose: phase 0 adds no caching, and each consumption runs the same activator path A01 and A02 prove for one, with A02's per-process-cache bite-proof guarding against a shared stale value; a many-activity run would assert nothing those rows do not |
