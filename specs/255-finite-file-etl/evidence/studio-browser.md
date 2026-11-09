# Integrated ETL Studio browser acceptance

Executed 2026-10-21 local time (2026-10-20 22:51 UTC), against the integrated working tree following `b47e59b332ea4823e7cf86e5b52029ddc43fa226`. T018/T029/T030/T032 acceptance, not a merge or CI result.

## Real journey

`pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtlStudio.ps1` started private, authenticated, rebuilt Workbench and Studio hosts with fresh SQLite stores, scoped file aliases and two source/lookup records. The one Chromium test passed in 1.8 minutes: **1 expected, zero unexpected, skipped or flaky**. Both owned hosts were stopped in teardown and independently confirmed absent.

Through the actual UI, the journey created a draft, demonstrated a duplicate-schema diagnostic, corrected it, saved, validated and published version 1. It selected the exact published tuple in a native Sequence containing RunEtl followed by WriteLine, used ordinary Review & publish, and ran the persisted executable from the Executables page. ETL reported Succeeded, two accepted records, zero rejected/filtered/outstanding records and two completed partitions. The native timeline showed Sequence, RunEtl and WriteLine each completed once; RunEtl preceded WriteLine.

[Sanitized machine receipt](studio-browser/receipt.json) retains exact definition/selection/execution IDs, measured layout, test totals and screenshot hashes. Raw authentication/network logs are retained only in the private fixture and are not committed.

## Keyboard and viewport evidence

The test uses keyboard activation for authoring and the native activity picker. A named, focusable read-only source-schema table region responds to ArrowRight/ArrowLeft and restores its horizontal scroll position. Both ETL definition and run views measured a 390px main width and 390px scroll width, with no uncontained overflowing ETL text/elements. Screenshots are actual 390×844 viewports, not full-page captures.

- [Published schema and focused table](studio-browser/etl-definition-mobile.png).
- [Successful ETL run on narrow viewport](studio-browser/etl-run-mobile.png).
- [Completed Sequence and following WriteLine](studio-browser/etl-native-sequence-completed.png), 1440×900.

Root visually inspected all three images. The existing shared Studio header is partly clipped at this narrow width; this receipt qualifies the ETL content, not all global Studio chrome. Wide tables intentionally scroll within their named regions. No page overflow is hidden to manufacture these assertions.

## Failure history and limits

Earlier attempts exposed test harness assumptions, ETL long-token overflow and a real missing compilation tenant in native publication review. Those attempts failed and are not counted as passes. The fixes and precise tenant mutation/restoration are recorded in [Workbench HTTP evidence](workbench-http.md); this successful rebuilt-host run verifies the repaired publication-review path end to end.

The browser exercises the successful Sequence path. Rejected/filtered rows and Flowchart continuation are covered by the separate rebuilt HTTP journey; fault/cancellation and recovery states have component/API/runtime evidence. No claim is made for other browsers, transient editor Test Run, generic task-runner pause, historical timeout causes, or exactly-once external effects. Fresh-head CI, final review and post-merge checks remain delivery gates.
