# T049: shared ordinary source selection

This increment extracts the existing ordinary source-reference resolver for reuse by recipient capture.
**T049 remains open and saved starts remain disabled.** The [machine receipt](broadcast-source-selection.json)
binds the three implementation/test files to source parent `0ca0ddfa3d499655789d1b04951e08bd8b2efc48`.

The dispatcher delegates its former selection logic to an internal helper, preserving all public constructors,
reference traversal, scope/selector/liveness order and refusal categories. Capture uses that same resolver for
published activation/slot selection. It records actual publication provenance independently of deduplicated
producer metadata. Explicit legacy null is available only when no reference and no selector exists; an
unselected binding with one real live reference captures that reference. No policy or recipient effect occurs.

Final restored whole Runtime: **2851 passed, 0 failed, 0 skipped**. Focused source/existing start cases:
66 passed, including nine new capture cases. Controls cover selected attribution, refusal after retirement
rather than substitution, legitimate legacy null, existing-reference attribution, missing/retired/wrong-scope/
expired/ambiguous sources and a second match beyond the first maximum page. Five behavioral mutations
remove capture, liveness, scope, legitimate-null and full-traversal guards; all fail without compilation errors
and were restored before the whole-project run. No public API change. Frozen authoring fingerprint remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Correctness, standards and acceptance source and final receipt reviews are clean. Full-PR review
remains zero rounds. This selection component does not establish accepted-cohort reference eligibility,
lease/GC/source/execution/dedup retention, saved-start admission or upgraded-host behavior. Capture must
run under the eventual full root-lease acceptance boundary. Those T049 gates, T050 activation, T051 actual
host/fresh-provider recovery, T045 and other story evidence, duplicate-semantics deletion/guards, final
current host/Studio, required checks, resulting-main verification and the third ADR0063 PR remain open.
#2898/#2907 and program production acceptance stay open; Orleans/task-runner remain deferred.
