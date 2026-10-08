# Pinned profile and executable identity investigation

## Status and scope

On 8 October 2026, root and independent review found a material publication-identity defect while verifying #2400. The production structured executable hasher omits the pinned `ActivityContract`. This report corrects the earlier [replay-safety audit](../../../docs/reports/runtime-db-access/replay-safety-audit.md); the response annotation remains unaccepted. No database was changed during evidence readback.

The bounded question is whether changing only the pinned side-effect profile changes the identity emitted by the normal workflow compiler, while existing stored artifacts retain their published interpretation. It does not claim corruption or overwrite of existing artifacts, a timing regression, or an accepted query reduction.

## Retained real-publication evidence

Two independent fresh SQLite stores from the combined-source captures at `188c3c2a39306b77b4e0410dabd551fab93158ae` contain the following `write-response` contracts. Root independently queried the retained executable rows read-only and decoded their relational identity fields. The contract JSON is equal after excluding only `sideEffectProfile` and `schemaFingerprint`.

| Property | Candidate | One-line External mutation |
|---|---|---|
| Pinned profile | ReplaySafe | External (omitted default) |
| Artifact ID | `artifact-9b4cc6803cc4` | `artifact-9b4cc6803cc4` |
| Artifact hash | `sha256:9b4cc6803cc480fc6679cafcb3f489c7a4c8325d657f97646914206920e7a134` | Same |
| Contract fingerprint | `sha256:d43450e3353c6b7c5816ad258137b6cf14b8ce3a3c335cabb693df9fcb790a2c` | `sha256:1e07789673dad3a468fc4125d6e4a02b49bf6abc50afb41be84256b8222086ee` |
| Contract version | `1.0.0+188c3c2a39306b77b4e0410dabd551fab93158ae` | Same |
| Persisted ContentJson SHA-256 | `0d35fc140f898e6b1170198b7261e85db05750a925e15eaa7cabfa599d6f3245` | `9fe45601ec6d18b02e015da565c71d6163115d41af38235f87452cd9115926e2` |

The older External baseline artifact `artifact-b542eafc273a` has a different hash. That earlier difference cannot be attributed solely to the profile: the controlled candidate-versus-mutation pair above has the same hash. The failed mutation's observation lifecycle is recorded separately in [collector finalization](collector-finalization.md). Its raw publication evidence remains valid; its final count assertion did not pass.

## Source-confirmed mechanism

At source `169facfadddf7c9513c427897e549e8718d41362`, `ActivityContract.ComputeFingerprint` includes a non-default side-effect profile. `WorkflowExecutableCompiler.CompileAsync` calls the structured `IWorkflowExecutableHasher.ComputeHash(root, inputContract, dependencies, cadence, variables, incidentStrategy)` overload. Its `WriteNode` omits the pinned contract. The legacy single-root overload includes the contract fingerprint, but that is not this production compilation path.

`EfWorkflowExecutableStore.SaveBatchAsync` compares an existing row's artifact hash, and treats an existing identical ID/hash as an immutable no-op. `PublishWorkflowRequestHandler.Handle` subsequently continues with the newly compiled object rather than reloading the retained payload. Thus the source supports a same-store stale-profile risk; the two captured stores above establish an identity collision across stores, not a same-store publication execution.

## Executed compiler regression

Root reviewed a test-only patch over `169facfadddf7c9513c427897e549e8718d41362`. The normal compiler compiles the same workflow and request twice; its existing metadata-enrichment seam changes only the pinned profile. Assertions confirm equal node/type/descriptor/bindings and different contract fingerprints before requiring a changed artifact hash and ID.

`Compiler_artifact_identity_changes_when_only_the_pinned_side_effect_profile_changes` executed once and failed at the intended artifact-hash inequality assertion: both hashes were equal. There were no skips, timeouts or errors. The failure establishes the regression; it is not a passing gate.

- Command: `dotnet test tests/essentials/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj -p:RestoreLockedMode=true --filter FullyQualifiedName~Compiler_artifact_identity_changes_when_only_the_pinned_side_effect_profile_changes --logger 'trx;LogFileName=results.trx' --results-directory <owned-evidence-directory>`.
- TRX SHA-256: `2700e31d45f79c1e461b13e38c7ae7ac4b0e1805f1dcaf0d9fe1b51a73cc25c0`.
- Test patch SHA-256 at execution: `3641e4dcae438323119db178dc35b9fa0f6e8047b2b2b2fae84ba977642ac7d5`.
- Source/build-input manifest and HEAD were unchanged during execution.

## Executed EF existing-row characterization

`Executable_idempotent_save_preserves_the_first_pinned_side_effect_profile` passed 1/1 through the real EF/SQLite store. It saved External, attempted to save ReplaySafe under the same ID/hash, and read back External with its original fingerprint. The first row was not overwritten. This confirms the existing-row store behavior; it is not a complete same-store publication/activation proof.

- Command: `dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj -p:RestoreLockedMode=true --filter FullyQualifiedName~Executable_idempotent_save_preserves_the_first_pinned_side_effect_profile --logger 'trx;LogFileName=results.trx' --results-directory <owned-evidence-directory>`.
- TRX SHA-256: `7e05ce7bad45e0bd451a4382a085754ae4926e07901d3059f71bf0ce716ad04d`.
- Patch SHA-256 at execution: `9a80bcb87636ee2db58114136c4c909dc8753a4922153a71f0f455ebc411129d`; the patch additionally includes the audit erratum, while source/build inputs and HEAD were unchanged during the run.

## Correction and compatibility decision

A reviewed bounded correction is to hash the effective side-effect profile for every compiled activity-contract node, including External. Hashing only ReplaySafe is insufficient: an old ambiguous ID may already hold ReplaySafe, so a newly compiled External workflow must also receive an unambiguous new identity.

Existing EF rows remain stored under their old IDs and ordinary start resolves that persisted pin. Portable-artifact reconciliation separately recomputes the content hash: adding this field rejects old exported closures containing activity contracts unless they are republished or a versioned compatibility policy is added. The control room has asked the owner to select between the narrow pre-GA export break, explicit versioned legacy-import support, and retiring the contingent response annotation. No choice is assumed; no production hash/store change is included in this proof. After both proof runs completed, the candidate ReplaySafe attribute was removed: `WriteHttpResponse.cs` is byte-identical to accepted main `249cd21329a13d6d67da5fd8e2b0974579cb044b` and again resolves External. This follows the spec publication-failure stop condition; it does not yet settle the final retain-or-retire decision. Existing databases and retained artifacts remain untouched.

Correction selection and post-correction publication, persistence, compatibility and program acceptance gates remain open. The identity defect is not repaired by a retry or by the separate passing EF characterization.

## Evidence retention

Private evidence is retained under the program journal's `2400-post2497-verification-v2/`: `profile-artifact-identity-audit.md`, `root-profile-artifact-readback.json`, `root-profile-artifact-summary.json`, `identity-compiler-before/` and `identity-store-before/` with source patch, source-input manifest, command log, receipt and actual TRX. Paths identify retained local evidence, not a portable test dependency.
