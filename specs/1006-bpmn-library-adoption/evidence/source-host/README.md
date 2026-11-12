# Actual source-host binding checkpoint

Source revision: `b2fc515ffc213e7ba96f4040769269f43b4c00d8`, including the green-main
Studio UX qualification merge. Default committed Workbench composition, fresh disposable SQLite,
published Model/Interchange/Semantics 0.5.0. This is an implementation checkpoint on draft PR3338,
not complete switch acceptance.

- [Build identity](build.json): exact revision, shell config digest, SDK and loaded binary digests.
- [HTTP journeys](http-journeys.json): normal registered import/export, submission, publication and
  runtime execution. Every scenario must succeed before `passed` is true. PT5S is observed durably
  Suspended before completion. Both same-execution signal catches are observed Suspended before
  one imported PublishEvent publisher, then both complete through their parallel join. All four
  scenarios complete with zero incidents.
- [Published contracts](published-contracts.json): actual recommended active publication/catalog
  records read in one SQLite transaction. Both complete fingerprint projections equal the unchanged
  frozen oracle; build metadata is retained separately. This verifies persisted publication data,
  not the Studio mirror or a browser journey.

Reproduce on a clean checkout of the named revision with the repository's normal .NET SDK/tooling:

1. Build `Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release` through the build-slot wrapper.
2. Use `e2e-tests/_ServerLifecycle.ps1` to allocate a fresh content root and free loopback URL and start
   the explicit Release assembly. Retain that ownership handle; stop only that process in `finally`.
3. Run `e2e-tests/bpmn/Test-BpmnLibraryAdoption.ps1 -BaseUrl <owned-url> -SourceRevision <revision>
   -EvidencePath <receipt.json>` with PowerShell. The script never starts/stops a server.
4. Run `python3 e2e-tests/bpmn/verify_published_contracts.py --database <owned-root>/elsa.db
   --source-revision <revision> --output <contracts.json>` against that host's actual catalog database.
   The verifier opens it read-only and does not regenerate the frozen file.

Development red controls were real failures, subsequently corrected:

| Trigger | Observed failure | Correction |
|---|---|---|
| Parent8c885 default/baseline composition | Authenticated import HTTP404 | Enable existing interchange feature. |
| Parent8c885 with interchange enabled | Actual native1.0.0+build rejected; CanExecute=false | Follow explicit active recommendation, accept stable build metadata. |
| Recommended-catalog fix, old timer literal | Actual publication HTTP400, VF-COER-001: PT0S cannot convert to TimeSpan | Bind invariant TimeSpan; retain original XML interval. |
| Old routing, two committed same-execution signal catches | Publisher outbox Delivered; both catches stayed Suspended, joinRunning | Explicit named-event exact-bookmark fan-out. |
| New exact-handle router control before implementation | Expected2 resumes, actual1 | Correct router; whole suite green. |

The separate verifier controls use complete backups of this actual database, never the source:
retire the BPMN publication, remove its publication, or move its definition outside the visible
scope. Each comparison refuses zero matching active/visible publications. The unchanged source
compares2/2. The normal e2e run does not mutate production or golden databases.

Local affected whole-project results: Design689, interchange130, Activities.Runtime663,
Workflows.Runtime2610; zero failures/skips. Independent incremental reviews corrected constructor
binary compatibility, publication-query fidelity and redundant mailbox-key changes, then converged.
The complete switch author-review loop is still pending.

Limits: each routing attempt snapshots its current recipients; retry can see a newly re-armed
listener, as in the existing at-least-once contract. No durable first-recipient snapshot or exactly-once
listener lifetime is claimed. No retry/re-arm/partial-delivery, combined BPMN durability, Studio,
CallActivity coded error/escalation, ordinary-task compensation or final adoption acceptance is
established by these happy-path receipts. The reader/replay/pruning/deletion/full-review gates remain
open, as do #2898/#2907 and the broader program. ADR0063 remains proposed.
