# Source and decision record

Baseline: main `851f9291f80061e78b2f7744282d6a2196175196`. Proposed contract; no new executed product proof.

## Approved ownership

Decision: reviewed nonsecret public intent plus separately supplied required private configuration; absent inputs refuse generation. [Sipke's explicit decision](https://github.com/elsa-workflows/elsa-foundation/issues/1959#issuecomment-6023232082) supersedes the pending choice in the original [discovery](../../docs/reports/runtime-composition/portable-unknown-settings.md).

Rationale: public intent cannot recover unknown original values; supplying a different host directory silently retains different unknown values. The destination must actually supply the matching private carrier. Alternative: classify/map every unknown field before portability, which the owner did not choose.

## Public versioning

Decision: additive portable envelope v1 containing unchanged authored composition v1, entered through separate options. Keep the existing pure selection planner and catalog schema.

Rationale: [SelectionJsonReader.ParseComposition](../../src/essentials/Modularity/Planning/Json/SelectionJsonReader.cs) strictly enumerates top-level fields. Hiding a declaration inside opaque settings/resources defeats its meaning; silently changing v1 defeats compatibility. A wrapper avoids upgrading every planner consumer or pairing multiple public sidecars. Selection acceptance preserves opaque content and is not a safety certificate.

## Private carrier and integrity

Decision: existing supported host JSON files plus a separate operator-owned private receipt. Public input/revision UUIDs are random, independent of private bytes. The receipt privately binds those tokens to complete inventory/byte hashes, context and exact accepted public bytes. It is never published publicly or installed as host configuration.

Rationale: [CompositionFileSource](../../src/essentials/Cli/CompositionFileSource.cs) captures supported siblings; [SourceSnapshot](../../src/essentials/Modularity/Planning/Bridge/SourceSnapshot.cs) freezes bytes and privately checks SHA256. [CompositionInputSnapshot](../../src/essentials/Cli/CompositionInputSnapshot.cs) captures supplied inputs. Reuse finite regular-file admission from [CompositionFileReader](../../src/essentials/Cli/CompositionFileReader.cs) for the opt-in lane. No global local-v1 limit change. Actual shell/environment labels remain private in receipts: existing shell identity grammar admits path-like strings, so portable output must redact those labels rather than serialize the existing preview unchanged.

Receipt evidence detects accidental substitution/drift within operator-owned input. It is not a signature, credential or proof against a malicious operator replacing both metadata and files. No public private-content hash, vault, remote service, encryption format or credential transporter is introduced.

## Replacement and coexistence

Decision: complete replacement after explicit rebind review; no field-level target merge. Shell/environment stay fixed; changing them requires new import. Rebind produces new artifacts/revision, preserving existing input and public artifacts.

Rationale: [CompositionCandidateBuilder](../../src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs) copies source bytes, patches supported reviewed paths/resources and reads activation back. Operators can prepare a complete private destination bundle, then explicitly acknowledge that its unknown values differ. Fresh-directory output avoids inventing arbitrary unknown-setting merge semantics. Silent destination fallback, transparent rebind and durable original-path association alone cannot deliver the approved outcome.

## Safety, JSON fidelity and inspection

Decision: private arrays/objects remain intact; existing unsupported public array edits still refuse. Public output uses the exact reviewed projection from [CompositionImporter](../../src/essentials/Modularity/Planning/Bridge/CompositionImporter.cs) and [SettingReviewReader](../../src/essentials/Modularity/Planning/Bridge/SettingReviewReader.cs). Portable acceptance/rebind must validate authored opaque settings/resources before public publication. Known physical persistence fields remain private even when reviewed. Review is a human assertion, not a secret detector; add no arbitrary owner prose to public declarations.

Inspection validates the portable association in the CLI parent and then uses existing candidate/explicit intended lanes. Candidate output needs a separate private receipt because reviewed edits can change bytes. [Spec189](../189-explicit-environment-inputs/contracts/cli-inspect-environment-v1.md)'s intended overlay remains inspection-only and does not supply arbitrary typed bundle transport, unknown-option runtime consumption or deployed attestation. Never reuse `SelectionDigest` for a complete public envelope: its catalog projection admits only strings/arrays/objects; full-byte SHA256 belongs privately in receipts.

## Constitution review

Framework §2.12 / Elsa §E4 remain deferred; this bounded contract ratifies no general settings taxonomy. CLI owns IO; Planning owns pure bridge/selection semantics. Framework §2.21.1 / Elsa §E1 preserve existing test subjects/objectives. No test removal, project/provider matrix or cadence change. Post-design review retains these gates. Signed ownership, live host attestation, activation and recovery remain separate program work.

Independent QA found public context-label leakage through legacy grammar, missing replacement disposition propagation, and an overclaim about unknown required secrets. Root addressed these by keeping actual context private, preserving/rendering origin/replacement disposition through accept/generate, and documenting operator-owned unknown-key completeness. Rebind retains previous receipt/public-context verification while permitting a different complete bundle.
