# Native template material and reference maintenance

Source parent `48d15bc1c256f1ee470766bf87686d1847815f64`.
The [machine receipt](broadcast-template-native-maintenance.json) binds fifteen source/contract/fixture files.
**This qualifies the explicit native-memory boundary only. EF, production-writer integration and qualified
default GC remain unfinished T049 work; publisher activation stays disabled.**

Four additive default-refusal operations on the existing template writer expose exact-store capability,
atomic template/reference save, doomed-reference maintenance and unreferenced-template maintenance.
`CanMaintainReferences` verifies the selected native artifact/reference instances and source-store ownership.
Legacy and mixed stores cannot acquire an implicit fallback. `CreateForMaintenance` explicitly binds those
stores and a clock. Its constructor is private: ordinary singleton DI retains the original parameterless
constructor, including hosts that replace artifact persistence with scoped services.

The native operations use the actual artifact → template → reference gates, with synchronous state changes
and no recipient effects. Template collection checks every physical workflow's wrapper/nested-node metadata
and every other template's exact direct/closed plus local/nested metadata edges. A physical parent retains
its dependency even before a runtime root is installed. Existing cohort artifact leases/deletion fencing
therefore protect templates through the actual retained artifact; no workflow guard is fabricated for a
template ID and no second cleanup ledger is introduced. Source deletion also compares its whole current
snapshot and refuses a restored or replaced row. Template deletion requires the exact current material
object and **no** remaining source reference, including doomed rows not yet removed. This intentionally
allows an additional sweep after the last physical parent is removed. Scans are finite and conservative,
per candidate, over the selected native material/reference dictionaries; no throughput claim is made.

Atomic save validates reference ID, current liveness, material/source identity and exact declared dependencies
before either mutation. It returns the actual winning live reference, preserving provenance. It cannot
restore retirement or extend expiry. Ordinary raw saves retain inert parent-first staging; they do not
grant atomic publication authority. Production publication/draft-test callers have not yet adopted this
new port, and their compatibility/lifecycle integration remains a required next step.

Final restored checks:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Whole Workflow Runtime | 2,926 | 0 / 0 |
| Whole Runtime EF | 1,318 | 0 / 0 |
| Core implementation-shape guard | 1 | 0 / 0 |

The 31 new Runtime cases cover actual winner adoption; first-expired/retired/conflicting source refusal;
malformed-ID and material-collision no-partial-write controls; physical workflow/parent referencers inserted
after candidate discovery; parent-first inert staging and missing-dependency publication refusal;
restored/replaced reference CAS; recreated-template identity; exact native ownership, legacy refusal and
requested cancellation. The one new case inside the EF test project deliberately uses the native harness:
actual cohort installation and actual collector keep template material through Delivering and Delivered with
an absent target, release it after sticky target retirement, and still collect an unrelated template.
It demonstrates the native cohort/GC bridge, not new EF template coordination.

Discovery maps every result: Runtime lists 2,915 cases, with three deferred theories expanding to fourteen
executions (2,915−3+14=2,926); EF lists and executes 1,318. Truncated display-name collisions are not
subtracted. Sixteen behavioral/lifetime mutations were detected and restored, including removal of the
physical dependency routes, current reference/material checks, expiry/dependency/ownership gates, cohort
reference-maintenance integration and preservation of ordinary scoped-persistence DI. No compile-error
mutation is counted. Core API delta +4/0 is tracked. Generated solution filters pass their local check.
Frozen authoring remains `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Review and whole-project testing corrected two issues before this receipt: blank reference IDs initially
allowed template material to be saved before reference validation; and a public bound constructor initially
made singleton DI capture scoped artifact replacements. Negative/positive controls and the existing
scoped-persistence composition test went red then green. All three final component source reviews are
clean; final receipt reviews are also clean. Full-PR rounds remain zero.

These real native state operations use deterministic discovery/write/collection orderings. They do not
inject a concurrent peer write inside a held gate or prove a fresh durable process, upgraded host, Studio
or production recovery. The previous pushed parent passed remote Build & test, architecture and maps in
run40380644671; new source needs its own remote checks. Full tier has not run on this draft. GitGuardian's
older public-source-digest incident38996611 remains unresolved; no protection/check is bypassed.

Next: implement EF conditional material/reference maintenance with real writer participation, integrate
publication/draft-test handoffs without changing authoring/state compatibility, then qualify default cohort
GC. T050 immutable input/exact bookmark/schema activation, T051 actual host recovery, T045/other stories,
duplicate-semantics deletion/guards, final current host/Studio, full review/exact-head checks/resulting-main
and third ADR0063 PR remain open. #2898/#2907/program production acceptance stays open; Orleans/task-runner
deferred. This is another verified increment on the sole existing adoption PR.
