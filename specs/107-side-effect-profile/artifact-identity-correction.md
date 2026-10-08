# Corrective work: pinned profile in workflow artifact identity

Issue: [#2515](https://github.com/elsa-workflows/elsa-foundation/issues/2515), owned by
[Feature #2398](https://github.com/elsa-workflows/elsa-foundation/issues/2398) in
[Program #2382](https://github.com/elsa-workflows/elsa-foundation/issues/2382).
Status: correction merged through #2517; resulting-main gates passed; current-format response fixture handoff pending.

## Requirement and owner decision

The normal compiler's structured executable hash omits the pinned activity profile. The
[executed regression](https://github.com/elsa-workflows/elsa-foundation/blob/dd7fe51d3333a90ae1703f7129a53ca30887cde7/specs/198-response-replay-safety/evidence/profile-artifact-identity.md)
demonstrates that a profile-only change produces the same artifact hash and ID. The EF store
then preserves the first same-ID/hash row. This violates the profile's behavioral identity
required by [ADR 0038](../../docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)
and the pinned contract in [Spec107](spec.md).

On 8 October 2026 the owner confirmed that Elsa Foundation has never been released or used,
and backwards compatibility for its workflow exports is not required. This removes the
compatibility stop. Change the current format directly. Do not add legacy import support,
dual hashes, compatibility adapters or data migrations. Existing databases and private
evidence remain preserved; this work does not rewrite stored rows.

## Corrective requirements

- C-FR-001: Otherwise identical compiled workflows with External and ReplaySafe pinned
  contracts must have different artifact hashes and derived IDs. Both values must appear
  explicitly in the new projection for nodes with a contract. A contractless node does not
  acquire a synthetic contract or profile.
- C-FR-002: Repeating equivalent compilation must remain deterministic. Existing exclusion
  of publication/source identity and existing canonical ordering remain intact.
- C-FR-003: Saving and reading both newly hashed variants through the real EF store must
  return their respective pinned profiles in either insertion order. No first-row collision
  may hide the second variant. Both corrected identities must differ from the retained
  pre-fix shared identity, and a seeded original row must remain unchanged and addressable
  while corrected variants are saved. This verifies collision prevention and non-overwrite;
  it does not require old-format import compatibility.
- C-FR-004: A valid current-format closure reconciles successfully. A closure whose profile
  and internally valid contract fingerprint change while its declared executable identity
  stays fixed must fail content-hash verification before it is persisted.
- C-FR-005: Profile changes in a placed activity-template root or a pinned child executable
  must change the consuming workflow identity through the existing tree/dependency paths.
- C-FR-006: Keep runtime policies, activity classifications, mandatory checkpoints and the
  Immediate default unchanged. The conditional response classification remains #2400.

## Implementation plan

Use the existing structured `WorkflowExecutableHasher.WriteNode` projection. For a non-null
`ActivityContract`, write its effective `SideEffectProfile` as a named string member for
External as well as ReplaySafe. Keep the existing JSON writer and deterministic ordering.
There is no need for a new service, storage schema, global hash version or compatibility path.

Use the existing compiler metadata-enrichment seam for the regression already retained in
#2400. Reuse existing hasher, EF artifact-store, template/dependency and import fixtures.
Update only fixtures whose actual compiled behavioral hashes change. Do not replace whole
test files from the response branch or transplant its pending annotation and unrelated proof.

The full contract fingerprint includes contract version and additional contract semantics.
Projecting all of it would require a broader behavioral-identity audit. This correction owns
the reproduced profile omission; it does not claim a complete audit of every contract field.

Constitution check: no Design dependency enters runtime execution; executable-only runtime
and canonical content-addressed publication remain the existing boundaries. No new draft
constitutional rule or default is adopted. The owner accepts the pre-release identity change.

## Verification and execution tasks

- [x] C001: Adopt the existing normal-compiler regression on current main; retain its failing
  pre-fix execution and source pin. Do not treat a timeout or unavailable check as that proof.
- [x] C002: Implement C-FR-001/002 and focused tests for both profile values and determinism.
- [x] C003: Verify actual EF persistence/readback in both insertion orders and current-format
  import success/profile tamper rejection, with no persisted partial import.
- [x] C004: Verify placed-template and child-dependency propagation through existing fixtures.
- [x] C005: Run complete affected suites, relevant rebuilt-host publication/HTTP e2e,
  architecture and generated-maps checks. Run local builds serially through the shared-machine
  wrapper; the canonical architecture CI gate may supply the complete restored-graph check.
  Preserve any existing #2293/#2185 failures separately. Update genuinely stale maps only.
- [x] C006: Root review, independent review, exact-head PR checks and resulting-main gates.
  Post the accepted evidence on the PR and issue before closing #2515.
- [ ] C007: Integrate the accepted correction into the retained #2400 response branch, adapt
  its owned publication fixtures to the current format, and resume its conditional proof.

The failing compiler regression is the causal before-fix control; a direct hasher mutation
can supplement it if the final tests do not demonstrate the complete changed path. Focused
passes do not replace complete affected suites or the rebuilt normal-host publication path.
Old exported closures need no compatibility proof. No query or latency gain is claimed for
the identity correction itself; those claims remain owned by #2400 and final #2413 evidence.

## Evidence boundaries

The compiler regression uses the normal compiler and changes only the pinned profile through
its existing metadata-enrichment seam. EF and reconciliation tests construct current-format
executables with the production structured hasher. They prove their respective persistence
and import boundaries; they are not described as compiler-output integration tests. The
rebuilt-host publishing lifecycle exercises real compilation, persistence and execution.
#2400 additionally owns the actual published response profile-pair proof before fresh query
counts. These checks are complementary and none is a latency measurement.

## Focused verification receipt

The unchanged production baseline is `6b36c94f4ef4791ba15cb29dd6c03a2460d11fb8`.
The root inspected the executed TRX outcomes and the final diff; an independent Luna Extra
High reviewer found the missing golden update, then confirmed its correction and no other
material in-scope finding. Full integration and PR gates remain pending.

| Check | Executed result |
|---|---|
| Normal compiler before correction | 1 failed at the intended profile-only hash inequality; no skip or timeout |
| Compiler identity, template and dependency propagation after correction | 3 passed |
| EF insertion orders and seeded-row preservation | 2 passed |
| Current-format import and profile tamper rejection | 2 passed |
| Compiler corpus before updating snapshots | 4 passed, 9 failed at changed identities |
| Compiler corpus after updating snapshots, normal mode | 13 passed |

All passing rows had zero skips. The nine updated JSON snapshots change only
`identity.artifactHash` and derived `identity.artifactId`; the root checked this structurally.
Missing restore assets and an initial missing test namespace were setup/build failures,
not executed test evidence. Their logs remain retained alongside the successful runs.

The normal-compiler pre-fix shared hash is
`sha256:a6e20acedd46dc3fff2408d5c6bdae3fc5ee8f34519b608f086f0563a9c544bf`.
The regression asserts that both corrected profiles differ from it. The production hasher
file SHA-256 changes from
`88aebe66d24c5814d0ec3c6b840b105656485d3c6379410d382cc5c864d51d70` to
`c861de3d6a81620328b9206cfc116a13dfbe86633d374bf1ca6bf152f2911d6a`.

The first complete publisher run at `f1efed929` executed 717 tests: 716 passed and one
inline hash-golden test failed, with zero skips. Its two structured hash pins have now been
updated for the same profile projection; its contract-fingerprint assertions stay unchanged.
The focused corrected test passed 1/1. The first CI run was cancelled after the known local
failure while preparing the corrected head; that cancellation is not accepted evidence.
The complete affected and PR gates must run on the corrected candidate before acceptance.

## Merged correction checkpoint

[PR #2517](https://github.com/elsa-workflows/elsa-foundation/pull/2517) merged as
`9c9f475929018d96a16fb3a24549a304a7612254`. The
[pre-merge receipt](https://github.com/elsa-workflows/elsa-foundation/pull/2517#issuecomment-6065881852)
records 3,843 full affected tests, rebuilt Workbench publication 24/24 and HTTP methods
4/4, architecture 635/635, Maps, exact-head CI and root/independent/CodeRabbit review.
Copilot did not supply a review and is not counted as approval. The
[resulting-main receipt](https://github.com/elsa-workflows/elsa-foundation/pull/2517#issuecomment-6066239722)
accepts CI 37820309119, Maps 37820308354, filters, Code Quality, Docker and the authorized
automatic preview workflow. These completed gates supersede the pending statements in the
historical focused receipt above; its failed attempts remain retained.

The response branch integrated this correction at `11c852be19a24fd173edb77ab2cdbfc0aa977c42`.
Its production tree equals the accepted main tree and keeps WriteHttpResponse External.
The merged compiler/golden/refusal checks passed 21/21, and EF profile/non-overwrite checks
passed 3/3, all without skips. Its rebuilt normal Workbench produced the
[fresh External fixture](../198-response-replay-safety/evidence/current-format-publication.md).
The current-runtime child import check and C007 completion are pending; candidate-specific
ReplaySafe guards remain intentionally unaccepted while the declaration is External.
