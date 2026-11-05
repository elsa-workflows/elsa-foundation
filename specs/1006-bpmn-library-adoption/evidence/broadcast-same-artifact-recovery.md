# Actual same-artifact shedding, key reuse and recovery

This bounded T051 case qualifies two saved publication bindings sharing one artifact-scoped
start key through real admission shedding, later acceptance and standard-host recovery.
T051 remains unchecked. Remaining scopes, other retention/GC orders, historical upgraded-host
recovery, final Studio, deletion, delivery and production acceptance remain open.

The source parent is `38f45eee937003374237af0ccc76aa8eb63f0df4` with an explicit worktree
candidate suffix. The [machine receipt](broadcast-same-artifact-recovery.json) binds five
harness/reader files and three actual receipts. All passing cases verify identical **1,382**
current source bindings and **631** binary/dependency bindings before/after. The standard
binary contains no recovery probe; whole databases/configuration remain private.

## Actual three-incarnation sequence

1. Normal authenticated APIs publish an artifact and a SideBySide publication of the exact
   same version into a distinct slot. Two eligible bindings have distinct publication/source
   identities. Two exact catches are armed and a signal publisher executes. The first owned
   host saves both start entries and both catches, then is killed before recipient dispatch.
   Checked state proves two binding IDs/captured sources but one artifact, key and target ID.
   Each capture maps to its actual public publication, activation, artifact hash and version.
2. A second probe host restarts on the same private database. It retains the default admission
   controller and configures its operator static limit to **one**. It starts a separate real
   API workflow whose two unmatched waits durably park. A scoped drain observer holds that
   actual command before its real admission charge is disposed. The observer acquires no
   synthetic permit and supplies no controller, actor or provider result. Actual load is
   **14 dispatch units**, with a live ambient charge in that separate command.
3. Source delivery waits for the actual load observation. Natural claim expiry/takeover
   advances the source from fence **one to two** under a different owner. The first saved
   binding reaches the real default start dispatcher with **no ambient admission exemption**.
   It returns **Deferred/IsShed** against load 14/limit 1. An independent checked snapshot must
   show no physical target execution, the same immutable cohort and the exact original key/ID.
4. The test releases the real load command and awaits its successful ordinary HTTP response.
   Only then does it release the first binding's real result to the router. The later saved
   binding calls the same default dispatcher with **load zero**, the **identical original key
   and target ID**, and its own binding ID. It returns **Accepted** and the sole target durably
   parks. The target's full source provenance equals that later binding's versioned capture.
   Release/HTTP-response/acceptance timestamps supplement this causal order. The entire checked
   source projection remains unchanged between refusal, acceptance and the next owned crash.
5. The second probe is killed before remaining source work. A third, standard host without
   the probe reclaims under another owner at fence **three** and completes only the original
   callbacks/join. Both saved entries converge onto the already accepted target. Its complete
   checked physical state and full API instance/activity records remain unchanged, with no
   additional execution, activity attempt or incident. Later exact waits/bindings remain
   excluded. The source records Delivered with the unchanged complete cohort.

This proves the first refusal did not consume the artifact-scoped key in either the real
in-process actor path or durable admission: the later binding genuinely accepts that same
key in the same host incarnation, and a fresh standard host subsequently recognizes the
same physical execution. It does not substitute a fake shed verdict, manually held admission
permit, changed clock/claim or edited application row. Nested commands are normally exempt
from shedding; this case explicitly observes a recovered source without that exemption.

## Verification and review

- Actual three-incarnation same-artifact case: **1 passed, 0 failed, 0 skipped**.
- Actual republish/collection and pre-dispatch renewal regressions: **2/0/0**, with identical
  source/build manifests.
- Extracted actual cohort/refusal/acceptance guards: **26/0/0** (three real positive observations
  and twenty-three invalid-input controls). These cover collapsed publication/binding captures,
  changed keys/artifacts/sources, ambient exemption, absent load or real shedding, a preexisting
  target, altered successor ownership, repeated binding, duplicate instead of acceptance,
  continued load, rewritten source and acceptance before the real load response.
- Retention guard controls: **21/0/0**; renewal guard controls: **9/0/0**;
  checked source/publisher/material/reference controls: **14/0/0** (expecting two start entries).
- Owned process controls: **10/0/0**; actual cleanup failure controls: **3/0/0**.
- Three separate locked probe, standard and checked-reader builds pass.

The **83** controls qualify their stated boundaries; guard-input controls are not production
source mutation coverage. Three independent component source/actual/portable audits are
clean. Full-PR rounds remain **zero**, with six as the bound.

Review fixed two harness assumptions before actual execution: strict snapshot provenance
lives under `sourceCapture.version/provenance`, and two binding entries differ from one unique
artifact/execution. An incorrect metadata-constant compile assumption was corrected to the
actual start-dispatch metadata contract before any actual host qualification. The first real
case passed; explicit public-capture mapping, actual load release/response timestamps and the
extracted guards/controls were then added before final stable-source runs. Earlier iterations
are not counted as the final source-bound qualification.

The [main host receipt](broadcast-same-artifact-host.json) carries common manifests.
[Republish/collection](broadcast-same-artifact-republished-cohort.json) and
[renewal](broadcast-same-artifact-renewed-claim.json) receipts reference those identical verified
manifests. All seven owned host processes across these three cases terminated. Existing user
backend and Studio remained running.

Production source, project graph and frozen authoring are unchanged. Architecture **590**,
locked Server restore and generated-map checks are inherited from the
[earlier supporting qualification](broadcast-competing-proposal-recovery.json), not rerun here.
The frozen fingerprint remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Supported redrive, remaining scopes and GC/acceptance race orders remain open, alongside
remaining T051/T045/native composites, actual historical upgraded-host recovery, final
current Studio, legacy writer/duplicate semantics deletion and guards, full-PR review,
exact-head required checks/protected merge/resulting-main and separate ADR 0063 acceptance.
#2898/#2907 and the program's production acceptance stay open. Orleans and the task-runner
follow-up are deferred.
