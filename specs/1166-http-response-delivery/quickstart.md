# Qualification
1. Publish HTTP Endpoint → Write HTTP Response; send a real request. Verify body plus Written evidence on its exact invocation.
2. Insert Delay. Send a synchronous request, observe 202, allow durable resume; verify Completed plus warning. Refresh/restart and retain evidence.
3. Inspect a legacy/non-HTTP or ambiguous multi-endpoint run; verify Unknown without a fabricated warning.
4. Replay observation appends and race them with projection writes; verify idempotency and retained facts in SQLite.
5. Verify Studio exact-invocation navigation, warning badge, Unknown and existing incident behavior across light/dark layouts.


## Qualification receipt (2026-10-30)
- Whole projects, no filters/skips: Runtime 3,052; Runtime API 209; HTTP unit 302; HTTP integration 100; Runtime EF 1,726; Architecture 592. Discovery lists: 3,041 / 209 / 296 / 100 / 1,726 / 583 respectively; runtime-expanded theory rows account for the differing discovery/execution totals (checkpoint refusal, encryption predicates, enum values, endpoint options, API lifetime/capability cases).
- Studio: all 198 files / 2,676 tests passed with one worker; typechecks, ESLint and token-aware Stylelint passed. Six light/dark browser cases passed, including reload/restore, exact invocation warning, Written and legacy Unknown. Rendered screenshots were inspected.
- Production frontend build passed unchanged bundle limits: authenticated Definitions 384.36 / 384.50 kB; Activity upgrades 375.50 / 376.00 kB. Warning text uses the readable warning foreground token.
- Rebuilt Workbench: `e2e-tests/http/Test-HttpResponseDelivery.ps1` passed with fresh SQLite, published and isolated Test Run ingress, actual timer-pump resumption, and restart readback of the same retained warning identity. It owns and stops only its own processes.
- Mutation proof: replacing the HTTP drain observer with a no-op made the exact timer/resume regression fail for missing evidence; restoring it passed all six new HTTP integration cases.
- Architecture restore/592 tests, generated solution-filter map check and whole `Elsa.Server.slnx --locked-mode` restore passed. Updated HTTP test project references regenerated its NuGet lock file.
- Initial whole HTTP run found nine deterministic command-budget failures from the new evidence reads/writes; budgets now explicitly include those commands. Initial frontend run caught a gradient-capable token in `color-mix`; fixed. Later high-load frontend attempts encountered existing asynchronous/timing failures; the complete serial run passed without exclusions, timeout changes or quarantines.

## Database command accounting
The HTTP inline reference adds 12 commands: post-drain state/endpoint reads (2), exact write read/CAS update (3), closure state read plus read/CAS append (4), and closure reconciliation reads (3). The non-HTTP reference adds one state scan per drain (one for WriteLine; two for fork/join). Budgets change to 348/102/102 for WriteLine, 218/118/118 for HTTP, and 258/185/185 for fork/join (Durable/Balanced/Ephemeral). This retains deterministic cost bounds; it does not change checkpoint placement or execution policy.

## Limits
Only a uniquely attributable endpoint can produce a closed-origin warning. Multiple endpoint invocations, old request payloads, unsupported writers, interrupted capture and transport faults without a retained fact remain Unknown. A server write does not prove client receipt. Observations are bounded and best effort (five-second capture cancellation budget), and never become activity faults or retry instructions.
