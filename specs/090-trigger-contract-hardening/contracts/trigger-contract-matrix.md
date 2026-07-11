# First-Party Trigger Contract Matrix

| Trigger | Authored capability / intent | Executable classification | Recognizing provider | Binding cardinality | Required publication projection | Invalid preflight examples | Intentional non-start |
|---|---|---|---|---|---|---|---|
| Event | Trigger-capable activity with literal event name | Trigger marker in executable metadata | Event provider | Exactly 1 | None | Missing/blank/non-literal event name; ambiguous provider claim | Not supported by current Event contract |
| Timer | Trigger-capable activity with literal interval | Trigger marker in executable metadata | Timer provider | Exactly 1 | One recurring interval schedule | Missing/blank/invalid/non-positive interval; missing future occurrence | Not supported by current Timer contract |
| Cron | Trigger-capable activity with literal cron expression | Trigger marker in executable metadata | Cron provider | Exactly 1 | One recurring cron schedule | Missing/blank/invalid expression; no future occurrence; ambiguous provider claim | Not supported by current Cron contract |
| HttpEndpoint | Trigger-capable activity; authored `CanStartWorkflow=true` activates start role | Trigger marker in executable metadata | HTTP endpoint provider | 1 per normalized method, or 0 when non-starting | Existing binding metadata and HTTP-specific uniqueness validation; route table remains a derived runtime projection | Missing/non-literal path; invalid methods/options; duplicate template+method | `CanStartWorkflow` absent or false → recognized with zero descriptors |

## Matrix invariants

- Every active first-party start trigger produces at least one usable binding.
- Timer and Cron additionally produce a materialized future schedule.
- Only an explicit provider-recognized empty result represents non-start behavior.
- Shared stimulus identity may fan out for Event/Timer/Cron; HTTP template+method uniqueness remains HTTP-owned.
- No matrix row requires Design data at runtime.
