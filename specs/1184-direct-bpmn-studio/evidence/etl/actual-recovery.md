# Actual Studio ETL service recovery — partial product acceptance

The owned application products at `41f1828cb5797c167701e14f5c366cf152dda75b` contain main `8dcbf2345`. Browser/controller fixes reuse those unchanged builds; their source/hash identities are retained separately. This milestone completes T021's actual authored RunEtl service publication/replay. T025's retained retry UI is implemented. **T026 remains open** for the completed-occurrence navigation gap below.

## Actual recovery proved

Studio created and published a two-shard HTTP → JSONL pipeline with a CSV lookup, then an ordinary Sequence workflow containing RunEtl. Studio selected that immutable worker for a BPMN service, set an explicit two-attempt hold policy, published the BPMN process and started it directly. No hidden worker or runtime state was inserted. A separate real API repetition of the authored service-publication request returned the identical immutable material and source.

The dependency fixture waits for nonempty physical JSONL output, then moves the actual retained CSV lookup aside. A subsequent file read fails with `etl.snapshot-read-failed`. BPMN holds the native fault. A changed pipeline version2 is published, the host restarts, and a fresh browser discovers the same held BPMN run through the catalog. BPMN → original native attempt → ETL fault inspection and the exact native-occurrence return link work.

Restoring the exact lookup bytes and retrying through Studio creates one replacement native worker and ETL run, both retaining pipeline version1. The completed acceptance run captures a real HTTP200 Accepted retry response and then drops it before the browser receives it. Reloaded Studio resumes that exact request and receives its retained replay. After another host restart, replay returns the same outcome again. The parent completes at revision4.

The read-only verifier checks24 whole provider stores over six boundaries (workflow runtime/design and ETL runtime/design). It verifies one model, two exact pinned native workers, two ETL runs, one accepted retry input, no extra revision after replay, preserved original failure and output files, four replacement JSONL records, a changed published pipeline version2, and unchanged version1 selections. ETL outbox dispositions are2 (`ProvenParentTerminal`), with acknowledgement timestamps; these are not reported as `Consumed`. Four verifier controls pass, including altered request identity, corrupted store and changed physical output refusal.

## Remaining product gap

A further read-only Studio inspection of the successful replacement fails to show its ETL link. Its actual completed RunEtl row has `PrivateState = null`, while its consumed typed trigger receipt and the ETL invocation/result address remain durably present. The original failed occurrence retains PrivateState and projects successfully. `ActivityExecutionStateViewService` currently returns `StateCleared` before calling any projector. The earlier component/synthetic controls did not cover this actual completed writer state.

Fix the read projection for that retained occurrence, then qualify it against these actual old completed stores and repeat the affected real navigation proof. Do not preserve private payloads merely to keep a UI link, invent a client-side run identity, or claim that current T026 acceptance is complete.

## Corrections and limits

The first owned run proved ordinary Studio authoring/publication but used HTTP503 exhaustion; that is a business-error outcome, not a technical-failure hold. Its evidence remains a separate control. A subsequent recovery run succeeded, but its driver waited for a misnamed restore acknowledgement, then removed a response-cut hook before the request was sent. Both harness errors were corrected; the later accepted-response/reload/restart case passes. The independent verifier initially used the wrong numeric Succeeded enum and was corrected from the Core declaration before its successful run. These failed observations remain distinguishable in the archive.

Actual screenshots of the held run, original ETL fault and completed parent were visually inspected at1440px. This is not renewed theme/responsive/keyboard, API/package, frozen-native, runtime-only, independent-review or final-head qualification. Claims/interruption, final gates, wider ETL/provider acceptance and Program2892 remain open. See [machine receipt](actual-recovery-qualification.json).

## Portable archive

[Download the365-file owned evidence archive](https://github.com/elsa-workflows/elsa-foundation/releases/download/contributor-preview-2026-10-07.1/elsa-bpmn-studio-etl-41f1828cb.zip) and its [checksum](https://github.com/elsa-workflows/elsa-foundation/releases/download/contributor-preview-2026-10-07.1/elsa-bpmn-studio-etl-41f1828cb.zip.sha256). Archive size11,488,569bytes; SHA256 `d15d210dc1321c23d16ebded098ff558fe246ddf083d993f9d90804c211034f1`.

The existing preview release's publication time is unchanged. Both assets were uploaded by the Codex App. The downloaded archive and every member were verified, then the archived verifier and all four mutation controls passed from a fresh extraction against all24 recovery stores. No application journey was rerun from the download. The failed completed-occurrence inspection is retained alongside the passed recovery; the archive explicitly keeps T026 and broader acceptance open. See [publication/download receipt](actual-recovery-archive.json).
