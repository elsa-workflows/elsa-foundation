# Plan: Studio UX acceptance

## Design
Add a focused Playwright acceptance suite alongside existing browser/live tests. A Python lifecycle runner builds the two real source hosts and copies their build outputs to a private temporary directory. Their content roots, all relative stores and Nuplane state are private; only local origin configuration changes. Allocate ports dynamically, sanitize inherited provider credentials, terminate only child process groups and retain safe reports outside disposable stores.

Use a separate Playwright config with explicit required base URL/owned-run marker, no skips or retries, one worker, retained traces and JSON/HTML reports. A runner verdict cross-checks the expected project/test inventory, successful outcomes and required screenshot/ARIA artifacts. Report tested merge/source SHA and PR head separately and preserve a failure report even during build/startup failure.

Select the UX job with existing Studio scope; add its tooling and host inputs to that scope. Make Build & test depend on selected Studio checks before any documentation-only return. No new required status setting or privileged PR workflow is needed.

Extend contributor/module guidance, PR template and the existing acceptance review lens with a single concrete UX acceptance record. The non-author reviewer reads actual artifacts, records exact head and open findings, and re-reviews after changes. Reuse org review identities and protocol. Machine success and reviewer judgment remain distinct evidence.

## Constitution and risk review
Framework §2.23.2 / ADR 0080: targeted behavior checks and adversarial gate tests; no tests mirroring prose. No public API, persistence model, domain seam, project, package or ratification changes. Draft constitution sections are not relied on. Untrusted PR source executes only in existing read-only, ephemeral CI context; no secrets or write token are added.

## Validation
Tooling unit tests cover routing, result omissions/skips/failures, lifecycle cleanup and exact identity; mutation controls break aggregate inputs and required artifacts. Run real hosts with source seed credentials and inspect actual captures. Qualify initial journeys on main and the two prerequisite UI heads as needed, recording differences. Run maps/affected checks and three independent review lenses; resolve surviving findings and repeat against final head. Hosted CI remains separately reported until green.

## Initial source dependencies (historical)

The strict Agent/creation assertions intentionally cover fixes in unmerged PRs #3769 and #3774. Main a7ed7ac6 reproduces the combined create/list defect (four theme/viewport Agent cases fail); that is negative qualification, not an accepted run. Use an owned local integration commit carrying both prerequisites to qualify the new proof without modifying those PRs or claiming their changes are on main. The quality PR must not be called accepted on main until the prerequisites land and the exact head is revalidated.

## Pilot refinement

Independent inspection of the composed narrow Connections capture found names wrapping one character per line after the earlier collection patch. Preserve the name column's readable minimum width within the existing horizontal table scroller and add a real-row width/height regression. Expand captures to show model and advanced controls inside Studio's inner scroll container; page-level fullPage images alone do not cover that content. This is a reproduced pilot defect and targeted prevention, not a new redesign.

## Concurrent prerequisite refinement (historical)

PR #3774 gained an owner-approved separate-tab refinement at c9a9c19cf while the pilot ran. The earlier accepted integration pilot therefore does not qualify that latest prerequisite. Update the composed journey to Designer/Inputs/Outputs/Outcomes with counts, inspect both empty Add/Cancel experiences and requalify the latest exact prerequisite head. Do not accept a fallback to the old combined label.

## Main-based rollout

Both UI prerequisites are now merged. Reconciled source `016b70ef0` includes main `e6550a960` and passes the eight real-host cases with independent screenshot inspection. Its old hosted failure preserved the missing pre-prerequisite behavior and correctly failed Build & test. The refreshed PR must still supply successful current-head hosted artifacts and resulting-main aggregate activation before closing #3776.
