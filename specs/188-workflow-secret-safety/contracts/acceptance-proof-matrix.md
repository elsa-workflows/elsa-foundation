# Acceptance proof matrix

Required for implementation. No row is claimed executed by this specification task. "Bite-proof" names the single
revert or mutation that must turn the row's key test red; a row whose test stays green under its bite-proof guards
nothing and must be reported as such in the PR. Slice numbers refer to [tasks.md](../tasks.md#delivery-slices).

| ID | Requirements | Proof | Expected observation | Bite-proof | Slice |
|---|---|---|---|---|---|
| A01 | FR-001, FR-004, SC-001 | Publish a definition binding a test activity's credential input to `Secret`; run with a fake `IRuntimeSecretResolver` (slice 3) and then the real bridge (slice 4) | Activity receives the value converted to the declared type; definition and version hold only the reference | Make the materializer emit the binding as an empty literal instead of `SecretRead`: activity-received assertion red | 2, 3, 4 |
| A02 | FR-002, SC-002 | Rotate the secret between two runs; and rotate while a run is suspended, then resume | Second run and the resumed activation see the new value; no republish | Cache the first resolved value in the activator per process: rotation assertion red | 3, 4 |
| A03 | FR-002, FR-011 | After the suspend/resume run, read every persisted activity state row | Rows hold the withheld envelope, never the value | Resolve in the materializer and put the value inline: persisted-row assertion red | 2, 3 |
| A04 | FR-005, US1.4 | Two tenants, same secret name, different values; run each tenant's instance | Each sees its own value; a global or across-scope context refuses before resolution | Replace the partition read with `"default"`: two-tenant assertion red | 3, 4 |
| A05 | FR-003, SC-006 | One run per failure code (NotFound, Inactive, Expired, Revoked, Deleted, TypeMismatch, ScopeMismatch, StoreUnavailable, Unauthorized, CorruptState) plus a conversion failure | Fault names the reference and code, contains no value and no `ResolvedSecret.Error` text; `IsRetryable` true only for StoreUnavailable | Map StoreUnavailable to non-retryable: classification row red. Pass `Error` into the message: no-store-detail row red | 3, 4 |
| A06 | FR-003, §E2.6.1 | Run a secret-bound workflow in a host without `SecretsWorkflows` | Activity waits with an activation-failure incident naming the missing composition; it is not faulted | Throw a plain fault instead: waiting-status assertion red | 3, 4 |
| A07 | FR-006 | Reconcile a test activity declaring sensitive and credential inputs; read the catalog and the authoring view | `InputDefinition` and `ActivityInputDescriptorView` carry the flags; an undeclared activity's catalog hash is unchanged | Write `false` instead of null: hash-stability assertion red | 5 |
| A08 | FR-007 | Compile each row of the effective-policy table, including explicit `false` against a declaration | Stricter policy applies; downgrade refused with `VF-ACT-005` | Remove the downgrade check: refusal assertion red | 5 |
| A09 | FR-008, FR-009, SC-003 | Push one definition with a literal on a credential input through all seven entry points; repeat with an expression and with a variable binding; repeat with a secret reference and with an empty string | Each of 7 refuses with `Inputs/CredentialLiteral`, node id and input name; nothing stored at draft save; secret and empty bindings accepted everywhere; a sensitive non-credential literal accepted | Remove the guard from one entry point at a time: that entry point's row red (seven separate bite-proofs) | 6 |
| A10 | FR-010 | Commit a state change carrying a present envelope that requires encryption | Commit refused with `VF-ACT-005` | Remove the backstop rule: refusal assertion red | 7 |
| A11 | FR-010 | Hydrate a `PolicyRequiresEncryption` withheld envelope | `VF-ACT-010`, no null hydration | Hydrate null instead: assertion red | 2, 3 |
| A12 | FR-011 | Read evidence, run inspector (activity inputs and executable bindings) and diagnostic snapshots for a secret-bound run | Reference plus withheld marker; no value; no surface throws on `Withheld` | Remove the withheld branch from one surface: that surface's assertion red | 2, 7 |
| A13 | FR-012 | Activity throws an exception whose message contains the value; separately returns an `ActivityFault` whose message contains it | Persisted fault, incident, captured logs and exported telemetry show `[secret:<name>]`, never the value | Skip mask registration in the activator: all four assertions red. Skip the `ActivityFault` masking only: the returned-fault assertion red | 8 |
| A14 | FR-013, SC-004 | Canary: 4 run shapes (success, faulting exception, suspend/resume, rotation) then scan 8 surfaces | Value appears 0 times across all surfaces and shapes | See A15 | 9 |
| A15 | FR-014, SC-005 | Disable each protection alone: (P1) withheld materialization, (P2) exception masking at the boundary, (P3) `ActivityFault` masking, (P4) mask registration | Each disablement turns the canary red on at least one surface; the PR records which surface caught it | The table itself; a protection whose disablement stays green is reported as unguarded | 9 |
| A16 | FR-015 to FR-017 | Studio vitest suites | Credential input resolves to the secret picker with literal entry unavailable; `password` hint resolves to the masked editor ahead of single-line; no prefill | Remove the credential filter, then the editor registration: each test red | 10 |
| A17 | FR-018 | Search spec 079 for `ISecretResolver` and `src/Elsa/Secrets` | No matches | Reintroduce one occurrence: the search finds it | 1 |

## Canary surfaces (FR-013) and how each is read

| Surface | Read as |
|---|---|
| Stored definitions and versions | design SQLite files (all files in the directory, including `-wal`) |
| Git export output | every file in the export working tree, plus `git log -p` output of the export branch (git stores blobs compressed, so a raw scan of `.git/objects` would see nothing) |
| Persisted instance and activity state | runtime SQLite files; see the encoding note below |
| Execution evidence | evidence store or captured evidence records |
| Inspector responses | serialized responses of the run inspector and executable inspector handlers |
| Diagnostic snapshots | inspection projections and diagnostic snapshot payloads |
| Runtime telemetry export | a `System.Diagnostics.ActivityListener` on the runtime activity sources, recording span names, tags, events and status (the pattern `tests/essentials/Workflows/Runtime/Tests/RuntimeEngineTracingTests.cs` uses) |
| Runtime-captured logs | capturing logger provider registered on the host for all categories |

**Encoding note**: runtime `ContentJson` writes CLR strings as Base64 of UTF-16LE (`RuntimeArtifactJson`,
`LosslessUtf16StringConverter`). The scanner searches, per surface, the raw UTF-8 value, its JSON-escaped form, and
all three Base64 alignments of both its UTF-8 and UTF-16LE encodings. Without this, a leaked fault message in the
runtime database is invisible to the scan and the canary passes while leaking. The A15 bite-proof is the scanner's
control: P2 disabled must be found in the runtime database through the UTF-16LE Base64 search, not only in logs.

**Mode note**: input snapshots are handled in both discrete and fused start/invoke stages. If the canary host can
select both, the canary runs in both; if it cannot, the PR says which mode ran.
