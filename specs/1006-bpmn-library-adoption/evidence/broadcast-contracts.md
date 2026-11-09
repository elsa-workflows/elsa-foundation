# T046 — exact source and immutable recipient contracts

The [source/check receipt](broadcast-contracts.json) binds this Core contract increment to source
hashes. Together with [checked actual-store preflight](broadcast-preflight.md), it completes T046.
Provider CAS/versioning, the processor seam, retention and enabled dispatch are T047–T051; this receipt
is not their implementation or host-recovery evidence.

`PostCommitDeliveryContext` carries one internally coherent durable claim, exact commit-specific
outbox identity, persistence scope and immutable intent digest. Owner/fence/times and the complete
source suffix must agree. It grants no independent authority. `IStimulusDeliveryStore` requires the
provider to revalidate the actual persisted source/intent, current owner/fence, publisher scope and
revision before installation, returning only the persisted complete winner. Boundary, stale-claim,
invalid-argument and requested/unrequested cancellation behavior are declared. The contextual Core
exception preserves its source ID/cause while keeping its outer message safe.

`StimulusDeliverySnapshot` version 1 stores explicit start and resume lists; present empty is distinct
from absence. It freezes caller collections and binding metadata, preserves every sorted same-artifact
binding with full pin/activation/slot/routing provenance, and retains each exact wait's owner/node/
resume target/stimulus/creation/expiry identity without its payload. It does not silently truncate.
Strict converters require all declared fields, including nullable scope and binding provenance, while
leaving existing legacy binding readers unchanged. Legacy null partition and Unknown run-kind facts
remain explicit. Test-run scope is preserved and compared against authoritative source facts.

The new source intent digest uses framed lossless UTF-16 fields, canonical metadata/JSON and exact
decimal coefficient/exponent normalization. No floating-point rounding is introduced. Review found
that reusing the old checkpoint numeric writer produced different source digests for equivalent
numeric JSON; seven controls went red, then green with a separate intent-only normalizer. The complete
historical checkpoint `Compute`/ordering/canonical-writer tail remains byte-identical. A dedicated
control retains its old numeric-spelling distinction. The source digest supplements exact provider
comparison; it never replaces authorization or a claim fence.

## Verification and limits

- 74 contract cases pass: source claim/coherent IDs; every intent facet; UTF-16/null framing; numeric,
  structural JSON and durable round trips; old replay protection; strict nested wire; immutable full
  cohorts and 300 waits without truncation; source/pin mismatch; exact wait generation; test/legacy scope.
- Whole Workflow Runtime: **2,735 passed, zero failed** after restoring all source mutations.
- Seven restored mutations fail: omit fence (1), lossy UTF-16 (1), rewrite old replay normalization (1),
  omit nullable scope presence (1), omit binding activation presence (1), omit wait generation (1),
  bypass intent number normalization (9). All changed files were restored byte-identically.
- The 54 intentional additive Core API entries preserve existing CLR signatures. No old public entry,
  checkpoint representation, authoring descriptor or publisher execution path is replaced here.
- Frozen authoring fingerprint remains
  `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
- Independent correctness, standards and acceptance component reviews are clean after fixes. These
  are not full-PR review rounds. Existing analyzer/NuGet warnings are not a clean-warning claim.

The current handler remains intent-only until T048, no provider implements this new port before
T047, and no publisher stamps/uses the new delivery protocol before T050. The actual preflight found
no owed legacy sends in its supplied stores; it does not prove mixed-writer quiescence or a broader
production inventory. T045, T024/T029, T047–T051, semantic deletion/API guards, current host/Studio,
full-PR review/required exact-head checks/resulting-main verification, ADR0063 and production
#2898/#2907 remain open.
